using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImGuiNET;
using GameHelper.Ui;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var root=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);var locale=args.Length>2?args[2]:"zh-Hant";Directory.CreateDirectory(output);
var text=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"GameHelper/Localization",locale+".json")))!;
var plugins=new List<PlayerSettingsShell.PluginEntry>();
foreach(var name in new[]{"Atlas2","AutoHotKeyTrigger","HealthBars","LootValue","PickupHelper","PlayerBuffBar","PreloadAlert","Radar","RitualWispAlert","LootTracker","PortalAccess"})
{
    var loc=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"Plugins",name,"Localization",locale+".json")))!;
    plugins.Add(new(name,text.GetValueOrDefault("terms."+name.ToLowerInvariant(),name),loc.GetValueOrDefault("plugin.description",""),true));
}
ImGui.CreateContext();var io=ImGui.GetIO();unsafe{io.NativePtr->IniFilename=null;}
io.DisplaySize=new Vector2(1200,860);io.DeltaTime=1/60f;
io.Fonts.AddFontFromFileTTF("C:/Windows/Fonts/msyh.ttc",18,default,io.Fonts.GetGlyphRangesChineseFull());
io.Fonts.GetTexDataAsRGBA32(out IntPtr tex,out int tw,out int th,out _);
var pixels=new byte[tw*th*4];Marshal.Copy(tex,pixels,0,pixels.Length);io.Fonts.SetTexID((IntPtr)1);
using var bg=Image.Load<Rgba32>(Path.Combine(root,"GameHelper/Assets/settings-landscape.png"));
var bgPixels=new byte[bg.Width*bg.Height*4];bg.CopyPixelDataTo(bgPixels);
ImGuiTheme.Apply();var shell=new PlayerSettingsShell((k,f)=>text.GetValueOrDefault(k,f));
int hides=0,quits=0,pluginDraws=0,enableChanges=0;bool enabled=true;
var windowSize=new Vector2(1160,820);
void Frame()
{
    ImGui.NewFrame();ImGui.SetNextWindowPos(new Vector2(20));ImGui.SetNextWindowSize(windowSize);
    ImGui.Begin("GameHelper###Preview",ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse);
    shell.Draw(plugins,(IntPtr)2,"2.8.0","等待遊戲啟動","F12",_=>{},_=>pluginDraws++,(_,v)=>{enableChanges++;enabled=v;},()=>hides++,()=>quits++);
    ImGui.End();ImGui.Render();
}
void Settle(){for(var i=0;i<4;i++)Frame();}
void Click(float x,float y)
{
    io.AddMousePosEvent(x,y);Frame();io.AddMouseButtonEvent(0,true);Frame();io.AddMouseButtonEvent(0,false);Frame();Frame();
}
void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
void Save(string name)
{
    using var image=new Image<Rgba32>((int)io.DisplaySize.X,(int)io.DisplaySize.Y,new Rgba32(17,24,33,255));
    var data=ImGui.GetDrawData();
    for(int list=0;list<data.CmdListsCount;list++)
    {
        var draw=data.CmdLists[list];
        for(int ci=0;ci<draw.CmdBuffer.Size;ci++)
        {
            var cmd=draw.CmdBuffer[ci];if(cmd.UserCallback!=IntPtr.Zero)continue;
            var isBg=cmd.TextureId==(IntPtr)2;
            for(uint j=0;j<cmd.ElemCount;j+=3)
            {
                var a=draw.VtxBuffer[(int)(draw.IdxBuffer[(int)(cmd.IdxOffset+j)]+cmd.VtxOffset)];
                var b=draw.VtxBuffer[(int)(draw.IdxBuffer[(int)(cmd.IdxOffset+j+1)]+cmd.VtxOffset)];
                var c=draw.VtxBuffer[(int)(draw.IdxBuffer[(int)(cmd.IdxOffset+j+2)]+cmd.VtxOffset)];
                DrawTriangle(image,a,b,c,cmd.ClipRect,isBg?bgPixels:pixels,isBg?bg.Width:tw,isBg?bg.Height:th);
            }
        }
    }
    image.SaveAsPng(Path.Combine(output,name+".png"));Console.WriteLine("Rendered actual settings shell: "+name);
}
Settle();Save("settings-home");
shell.Page="mapping";Settle();Save("settings-mapping");
Check(PlayerSettingsShell.GroupOf("PortalAccess")=="mapping","Portal interaction settings appear under maps and earnings");
shell.Page="exploration";Settle();Save("settings-exploration");
shell.Search=locale=="zh-CN"?"雷达":"雷達";Settle();Save("settings-search");
shell.Search="";shell.Page="home";shell.RequestQuit();Settle();Save("settings-dialog");
// Escape must cancel the modal, without invoking the quit callback.
io.AddKeyEvent(ImGuiKey.Escape,true);Frame();io.AddKeyEvent(ImGuiKey.Escape,false);Frame();
Check(quits==0,"Escape cancels quit");
// Disabled plugin pages must never execute unloaded plugin settings code.
shell.Page="plugin:LootTracker";var iTracker=plugins.FindIndex(p=>p.Id=="LootTracker");
plugins[iTracker]=plugins[iTracker] with{Enabled=false};var before=pluginDraws;Settle();
Check(before==pluginDraws,"Disabled plugin settings are not rendered");
plugins[iTracker]=plugins[iTracker] with{Enabled=true};Settle();Check(pluginDraws>before,"Enabled plugin settings render");
shell.Page="home";windowSize=new Vector2(980,680);io.DisplaySize=new Vector2(1020,720);Settle();Save("settings-small");
Check(PlayerSettingsShell.GroupOf("LootTracker")=="mapping"&&PlayerSettingsShell.GroupOf("NewCommunityPlugin")=="other","Known and community plugins stay reachable");
Check(hides==0&&quits==0&&enableChanges==0&&enabled,"Rendering does not change plugin state or close the app");
// Exercise actual ImGui hit testing for navigation, search results, enable and chrome.
windowSize=new Vector2(1160,820);io.DisplaySize=new Vector2(1200,860);shell.Page="home";Settle();
Click(142,212);Check(shell.Page=="mapping","Sidebar opens maps and earnings");
shell.Search=locale=="zh-CN"?"雷达":"雷達";Settle();Click(365,306);
Check(shell.Page=="plugin:Radar"&&shell.Search.Length==0,"Search result opens plugin and clears query");
shell.Page="plugin:LootTracker";plugins[iTracker]=plugins[iTracker] with{Enabled=false};Settle();
// The enable row follows back button, title and description.
Click(287,271);Check(enableChanges==1&&enabled,"Plugin enable checkbox dispatches enable callback");
shell.Page="home";Settle();Click(400,350);Check(hides==1,"Return to game hides settings");
Click(1132,60);Settle();Check(ImGui.IsPopupOpen("GameHelperCloseConfirmation",ImGuiPopupFlags.AnyPopupId),"Close opens confirmation");
Click(450,516);Check(quits==0,"Cancel keeps app running");
Click(1132,60);Settle();Click(735,516);Check(quits==1,"Confirm invokes quit exactly once");
Click(1082,60);Check(hides==2,"Minimize hides settings without quitting");
ImGui.DestroyContext();

static float Cross(Vector2 a,Vector2 b)=>a.X*b.Y-a.Y*b.X;
static void DrawTriangle(Image<Rgba32> img,ImDrawVertPtr a,ImDrawVertPtr b,ImDrawVertPtr c,Vector4 clip,byte[] texture,int tw,int th)
{
    var area=Cross(b.pos-a.pos,c.pos-a.pos);if(MathF.Abs(area)<.00001f)return;
    int x0=(int)MathF.Max(MathF.Max(0,clip.X),MathF.Floor(MathF.Min(a.pos.X,MathF.Min(b.pos.X,c.pos.X))));
    int y0=(int)MathF.Max(MathF.Max(0,clip.Y),MathF.Floor(MathF.Min(a.pos.Y,MathF.Min(b.pos.Y,c.pos.Y))));
    int x1=(int)MathF.Min(MathF.Min(img.Width,clip.Z),MathF.Ceiling(MathF.Max(a.pos.X,MathF.Max(b.pos.X,c.pos.X))));
    int y1=(int)MathF.Min(MathF.Min(img.Height,clip.W),MathF.Ceiling(MathF.Max(a.pos.Y,MathF.Max(b.pos.Y,c.pos.Y))));
    for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
    {
        var p=new Vector2(x+.5f,y+.5f);var w0=Cross(b.pos-p,c.pos-p)/area;var w1=Cross(c.pos-p,a.pos-p)/area;var w2=1-w0-w1;
        if(w0<0||w1<0||w2<0)continue;
        var uv=a.uv*w0+b.uv*w1+c.uv*w2;
        int tx=Math.Clamp((int)(uv.X*tw),0,tw-1),ty=Math.Clamp((int)(uv.Y*th),0,th-1),t=(ty*tw+tx)*4;
        float Channel(int shift)=>((a.col>>shift)&255)*w0+((b.col>>shift)&255)*w1+((c.col>>shift)&255)*w2;
        var alpha=Channel(24)/255f*texture[t+3]/255f;if(alpha<=0)continue;
        var old=img[x,y];byte Blend(int shift,int channel,byte prior)=>(byte)Math.Clamp(Channel(shift)*texture[t+channel]/255f*alpha+prior*(1-alpha),0,255);
        img[x,y]=new Rgba32(Blend(0,0,old.R),Blend(8,1,old.G),Blend(16,2,old.B),255);
    }
}
