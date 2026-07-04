namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    public class Stack : ComponentBase
    {
        public Stack(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the current number of items in the stack.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        ///     Gets the normal stack cap (backpack and normal stash tabs).
        /// </summary>
        public int MaxStack { get; private set; }

        /// <summary>
        ///     Gets the currency stash tab stack cap.
        /// </summary>
        public int MaxStackTab { get; private set; }

        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Stack Count: {this.Count}");
            ImGui.Text($"Max Stack: {this.MaxStack}");
            ImGui.Text($"Max Stack (Currency Tab): {this.MaxStackTab}");
        }

        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StackOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Count = data.Count;

            // The StackSizeData descriptor is shared per base item type and does
            // not change over the lifetime of this component's address, so it is
            // only re-read when the address changes (mirrors Charges.PerUseCharge).
            if (hasAddressChanged)
            {
                if (data.StackSizeDataPtr != IntPtr.Zero)
                {
                    var sizeData = reader.ReadMemory<StackSizeDataOffsets>(data.StackSizeDataPtr);
                    this.MaxStack = sizeData.MaxStack;
                    this.MaxStackTab = sizeData.MaxStackTab;
                }
                else
                {
                    this.MaxStack = 0;
                    this.MaxStackTab = 0;
                }
            }
        }
    }
}
