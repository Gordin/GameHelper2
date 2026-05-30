namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    public class Stack : ComponentBase
    {
        public Stack(IntPtr address)
            : base(address) { }

        public int Count { get; private set; }

        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Stack Count: {this.Count}");
        }

        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StackOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Count = data.Count;
        }
    }
}
