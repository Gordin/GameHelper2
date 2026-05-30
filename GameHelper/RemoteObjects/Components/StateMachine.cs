namespace GameHelper.RemoteObjects.Components
{
    using GameOffsets.Natives;
    using GameOffsets.Objects.Components;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;

    public class StateMachine : ComponentBase
    {
        public StateMachine(IntPtr address) : base(address) { }

        private const int StateStructSize = 0xC0;

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

        private const int MaxStateMachineStates = 256;

        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StateMachineComponentOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;

            var stateValues = reader.ReadStdVector<long>(data.StatesValues);
            var statesCount = stateValues.Length;
            if (statesCount > MaxStateMachineStates)
            {
                Console.WriteLine($"[StateMachine] Suspicious state count {statesCount} at 0x{this.Address.ToInt64():X}; capping to {MaxStateMachineStates} (audit F-120).");
                statesCount = MaxStateMachineStates;
            }

            var statesPtr = reader.ReadMemory<IntPtr>(data.StatesPtr + 0x10);
            if (statesPtr == IntPtr.Zero)
            {
                this.States = [];
                return;
            }

            var statesList = new List<StateMachineState>(statesCount);
            for (var i = 0; i < statesCount; i++)
            {
                var stateNameAddr = statesPtr + i * StateStructSize;
                var nativeContainer = reader.ReadMemory<StdString>(stateNameAddr);
                var stateName = reader.ReadStdString(nativeContainer);
                statesList.Add(new StateMachineState(stateName, stateValues[i]));
            }

            this.States = statesList;
        }
    }

    public class StateMachineState(string name, long value)
    {
        public string Name { get; } = name;
        public long Value { get; } = value;

        public override string ToString() => $"{Name}: {Value}";
    }
}

