namespace GameHelper.RemoteObjects.Components
{
    using GameOffsets.Objects.Components;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;

    public class StateMachine : ComponentBase
    {
        public StateMachine(IntPtr address) : base(address) { }
        private const int ValueSize = sizeof(long);

        public IReadOnlyList<StateMachineState> States { get; private set; } = [];

        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"State Count: {States.Count}");
            for (int i = 0; i < States.Count; i++)
            {
                var state = States[i];
                ImGui.Text($"State[{i}]: Name='{state.Name}', Value={state.Value}");
            }
        }

        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StateMachineComponentOffsets>(this.Address);

            this.OwnerEntityAddress = data.Header.EntityPtr;
            long count = data.StatesValues.TotalElements(ValueSize);

            var states = new List<StateMachineState>((int)count);

            // Read all state values at once
            int byteCount = (int)(count * ValueSize);
            var valueBytes = reader.ReadMemoryArray<byte>(data.StatesValues.First, byteCount);

            long[] values = new long[count];
            Buffer.BlockCopy(valueBytes, 0, values, 0, byteCount);

            for (long i = 0; i < count; i++)
            { // gooby pls 
                // cant figure out how to get state names  
                states.Add(new StateMachineState("todo", values[i]));
            }
            this.States = states;
        }
    }

    public class StateMachineState(string name, long value)
    {
        public string Name { get; } = name;
        public long Value { get; } = value;

        public override string ToString() => $"{Name}: {Value}";
    }

}