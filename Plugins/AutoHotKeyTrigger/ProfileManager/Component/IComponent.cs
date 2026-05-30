// <copyright file="IComponent.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Component
{
    /// <summary>
    ///     A partial condition that can't be used on it's own and
    ///     adds more logic to a <see cref="DynamicConditions"/>.
    /// </summary>
    public interface IComponent
    {
        /// <summary>
        ///     Executes the component
        /// </summary>
        /// <returns>returns true or false based on the component states</returns>
        public bool execute(bool isConditionValid);

        /// <summary>
        ///     Display the component via ImGui widgets.
        /// </summary>
        /// <param name="expand">should display the component in expanded form or non expanded form.</param>
        public void Display(bool expand);

        /// <summary>
        ///     Clones the current component.
        /// </summary>
        /// <returns>Deep clone of this component.</returns>
        public IComponent Clone();
    }
}
