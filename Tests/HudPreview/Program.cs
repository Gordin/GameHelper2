using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImGuiNET;
using LootTracker;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var root=Path.GetFullPath(args[0]); var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var text=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"Plugins/LootTracker/Localization/zh-Hant.json")))!;
ImGui.CreateContext();
var io=ImGui.GetIO(); io.DisplaySize=new Vector2(786,130);io.DeltaTime=1/60f;
io.Fonts.AddFontFromFileTTF("C:/Windows/Fonts/msyh.ttc",20,default,io.Fonts.GetGlyphRangesChineseFull());
io.Fonts.GetTexDataAsRGBA32(out IntPtr tex,out int tw,out int th,out int bpp);
var pixels=new byte[tw*th*4];Marshal.Copy(tex,pixels,0,pixels.Length);io.Fonts.SetTexID((IntPtr)1);
var models=new [] {
    new HudModel {Hourly="0.0 / 時",SessionTime="00:02"},
    new HudModel { MapCount=3, AverageTime="04:26",AverageValue="36.7 Ex",Total="110.0",Hourly="275.0 / 時",SessionTime="24:00", Rows=new []{new HudRow("失落高塔","03:41","42.0 Ex"),new HudRow("草原","05:12","38.0 Ex"),new HudRow("溪流","04:25","30.0 Ex")} }
};
for(int sample=0;sample<models.Length;sample++)
{
    for(int frame=0;frame<3;frame++)
    {
        ImGui.NewFrame();CompactHud.Draw(models[sample],new Vector2(3,3),780,1,.65f,true,(k,f)=>text.GetValueOrDefault(k,f));ImGui.Render();
    }
    using var image=new Image<Rgba32>(786,130,new Rgba32(55,54,49,255));
    var data=ImGui.GetDrawData(); int triangles=0;
    for(int list=0;list<data.CmdListsCount;list++)
    {
        var draw=data.CmdLists[list];
        for(int ci=0;ci<draw.CmdBuffer.Size;ci++)
        {
            var cmd=draw.CmdBuffer[ci]; if(cmd.UserCallback!=IntPtr.Zero)continue;
            for(uint j=0;j<cmd.ElemCount;j+=3)
            {
                var a=draw.VtxBuffer[(int)(draw.IdxBuffer[(int)(cmd.IdxOffset+j)]+cmd.VtxOffset)];
                var b=draw.VtxBuffer[(int)(draw.IdxBuffer[(int)(cmd.IdxOffset+j+1)]+cmd.VtxOffset)];
                var c=draw.VtxBuffer[(int)(draw.IdxBuffer[(int)(cmd.IdxOffset+j+2)]+cmd.VtxOffset)];
                DrawTriangle(image,a,b,c,cmd.ClipRect,pixels,tw,th);triangles++;
            }
        }
    }
    image.SaveAsPng(Path.Combine(output,sample==0?"hud-empty.png":"hud-example.png"));
    Console.WriteLine($"Rendered actual ImGui HUD sample {sample}: {triangles} triangles.");
}
ImGui.DestroyContext();

static float Cross(Vector2 a,Vector2 b)=>a.X*b.Y-a.Y*b.X;
static void DrawTriangle(Image<Rgba32> img,ImDrawVertPtr a,ImDrawVertPtr b,ImDrawVertPtr c,Vector4 clip,byte[] texture,int tw,int th)
{
    var area=Cross(b.pos-a.pos,c.pos-a.pos); if(MathF.Abs(area)<.00001f)return;
    int x0=(int)MathF.Max(MathF.Max(0,clip.X),MathF.Floor(MathF.Min(a.pos.X,MathF.Min(b.pos.X,c.pos.X))));
    int y0=(int)MathF.Max(MathF.Max(0,clip.Y),MathF.Floor(MathF.Min(a.pos.Y,MathF.Min(b.pos.Y,c.pos.Y))));
    int x1=(int)MathF.Min(MathF.Min(img.Width,clip.Z),MathF.Ceiling(MathF.Max(a.pos.X,MathF.Max(b.pos.X,c.pos.X))));
    int y1=(int)MathF.Min(MathF.Min(img.Height,clip.W),MathF.Ceiling(MathF.Max(a.pos.Y,MathF.Max(b.pos.Y,c.pos.Y))));
    for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)
    {
        var p=new Vector2(x+.5f,y+.5f);
        var w0=Cross(b.pos-p,c.pos-p)/area;var w1=Cross(c.pos-p,a.pos-p)/area;var w2=1-w0-w1;
        if(w0<0||w1<0||w2<0)continue;
        var uv=a.uv*w0+b.uv*w1+c.uv*w2;
        int tx=Math.Clamp((int)(uv.X*tw),0,tw-1),ty=Math.Clamp((int)(uv.Y*th),0,th-1),t=(ty*tw+tx)*4;
        float Channel(int shift)=>((a.col>>shift)&255)*w0+((b.col>>shift)&255)*w1+((c.col>>shift)&255)*w2;
        var alpha=Channel(24)/255f*texture[t+3]/255f;if(alpha<=0)continue;
        var old=img[x,y];
        byte Blend(int shift,int channel,byte prior)=>(byte)Math.Clamp(Channel(shift)*texture[t+channel]/255f*alpha+prior*(1-alpha),0,255);
        img[x,y]=new Rgba32(Blend(0,0,old.R),Blend(8,1,old.G),Blend(16,2,old.B),255);
    }
}
