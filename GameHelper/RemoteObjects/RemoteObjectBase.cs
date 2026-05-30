// <copyright file="RemoteObjectBase.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using ImGuiNET;
    using Ui;
    using Utils;

    /// <summary>
    ///     Points to a Memory location and reads/understands all the data from there.
    ///     CurrentAreaInstance in remote memory location changes w.r.t time or event. Due to this,
    ///     each remote memory object requires to implement a time/event based coroutine.
    /// </summary>
    public abstract class RemoteObjectBase
    {
        private readonly bool forceUpdate;
        private readonly object updateLock = new();
        private IntPtr address;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RemoteObjectBase" /> class.
        /// </summary>
        internal RemoteObjectBase()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RemoteObjectBase" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        /// <param name="forceUpdate">
        ///     True in case the object should be updated even if address hasn't changed.
        /// </param>
        /// <param name="skipFirstUpdate">
        ///      Pass true if derived object takes the responsibility of executing the <see cref="UpdateData"/>
        ///      in constructor. This fixes the issue where derived object is introducing a new dependency
        ///      and that dependency isn't initialized until the derived object constructor is executed.
        /// </param>
        internal RemoteObjectBase(IntPtr address, bool forceUpdate = false, bool skipFirstUpdate = false)
        {
            this.forceUpdate = forceUpdate;
            if (skipFirstUpdate)
            {
                this.address = address;
            }
            else
            {
                this.Address = address;
            }
        }

        /// <summary>
        ///     Gets or sets the address of the memory location.
        /// </summary>
        public IntPtr Address
        {
            get
            {
                lock (this.updateLock)
                {
                    return this.address;
                }
            }

            set
            {
                bool hasAddressChanged;
                lock (this.updateLock)
                {
                    hasAddressChanged = this.address != value;
                    if (!(hasAddressChanged || this.forceUpdate))
                    {
                        return;
                    }

                    this.address = value;
                }

                // F-095: UpdateData/CleanUpData run OUTSIDE the lock. Holding updateLock
                // for the duration of an arbitrary memory-read sequence would block every
                // reader of Address.get. The try/catch wraps the post-lock work so derived
                // class throws still get logged + swallowed (Phase 1 C1a behavior preserved).
                try
                {
                    if (value == IntPtr.Zero)
                    {
                        this.CleanUpData();
                    }
                    else
                    {
                        using var _ = PerformanceProfiler.Profile(GetType().FullName ?? string.Empty, "UpdateData");
                        this.UpdateData(hasAddressChanged);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{this.GetType().Name}.Address.set] {ex}");
                }
            }
        }

        /// <summary>
        ///     Converts the <see cref="RemoteObjectBase" /> to ImGui Widget via reflection.
        ///     By default, only knows how to convert <see cref="address" /> field
        ///     and <see cref="RemoteObjectBase" /> properties of the calling class.
        ///     For details on which specific properties are ignored read
        ///     <see cref="RemoteObjectBase.GetToImGuiMethods" /> method description.
        ///     Any other properties or fields of the derived <see cref="RemoteObjectBase" />
        ///     class should be handled by that class.
        /// </summary>
        internal virtual void ToImGui()
        {
            var propFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var properties = RemoteObjectBase.GetToImGuiMethods(this.GetType(), propFlags, this);
            ImGuiHelper.IntPtrToImGui("Address", this.address);
            foreach (var property in properties)
            {
                if (ImGui.TreeNode(property.Name))
                {
                    property.ToImGui?.Invoke(property.Value, null);

                    ImGui.TreePop();
                }
            }
        }

        /// <summary>
        ///     Reads the memory and update all the data known by this Object.
        /// </summary>
        /// <param name="hasAddressChanged">
        ///     true in case the address has changed; otherwise false.
        /// </param>
        protected abstract void UpdateData(bool hasAddressChanged);

        /// <summary>
        ///     Knows how to clean up the object.
        /// </summary>
        protected abstract void CleanUpData();

        /// <summary>
        ///     Iterates over properties of the given class via reflection
        ///     and yields the <see cref="RemoteObjectBase" /> property name and its
        ///     <see cref="RemoteObjectBase.ToImGui" /> method. Any property
        ///     that doesn't have both the getter and setter method are ignored.
        /// </summary>
        /// <param name="classType">Type of the class to traverse.</param>
        /// <param name="propertyFlags">flags to filter the class properties.</param>
        /// <param name="classObject">Class object, or null for static class.</param>
        /// <returns>Yield the <see cref="RemoteObjectBase.ToImGui" /> method.</returns>
        internal static IEnumerable<RemoteObjectPropertyDetail> GetToImGuiMethods(
            Type classType,
            BindingFlags propertyFlags,
            object? classObject
        )
        {
            var methodFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var properties = classType.GetProperties(propertyFlags).ToList();
            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                if (Attribute.IsDefined(property, typeof(SkipImGuiReflection)))
                {
                    continue;
                }

                var propertyValue = property.GetValue(classObject);
                if (propertyValue == null)
                {
                    continue;
                }

                var propertyType = propertyValue.GetType();

                if (!typeof(RemoteObjectBase).IsAssignableFrom(propertyType))
                {
                    continue;
                }

                yield return new RemoteObjectPropertyDetail
                {
                    Name = property.Name,
                    Value = propertyValue,
                    ToImGui = propertyType.GetMethod("ToImGui", methodFlags)
                };
            }
        }

        /// <summary>
        /// Attribute to put on the properties that you want to skip in <see cref="GetToImGuiMethods"/> method.
        /// </summary>
        [AttributeUsage(AttributeTargets.Property)]
        protected class SkipImGuiReflection : Attribute
        {
        }
    }
}