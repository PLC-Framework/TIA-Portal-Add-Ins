using System;
using System.Collections.Generic;

using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

using AddIn.Shared.Adapters;

namespace AddIn.Adapters
{
    /// <summary>
    /// IGroupNode over TIA's group compositions.
    ///
    /// The four families (blocks, technology objects, tag tables, types) share no base
    /// type and no interface, even though each one offers the same Find/Create pair, so
    /// one thin overload per family is unavoidable. What is NOT repeated is the walk:
    /// that lives once in AddIn.Shared.Actions.CreateProjectHierarchyAction.
    /// </summary>
    internal sealed class TiaGroupNode : IGroupNode
    {
        private readonly Func<string, IGroupNode> _findOrCreate;

        private TiaGroupNode(Func<string, IGroupNode> findOrCreate) => _findOrCreate = findOrCreate;

        public IGroupNode FindOrCreate(string name) => _findOrCreate(name);

        /// <summary>
        /// The group roots of the selected device, or null when it is not a PLC.
        /// </summary>
        public static HierarchyTargets TargetsFor(DeviceItem deviceItem)
        {
            PlcSoftware plc = deviceItem?.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            if (plc == null) return null;

            return new HierarchyTargets
            {
                Blocks = From(plc.BlockGroup?.Groups),
                TechnologyObjects = From(plc.TechnologicalObjectGroup?.Groups),
                TagTables = From(plc.TagTableGroup?.Groups),
                Types = From(plc.TypeGroup?.Groups),
                SoftwareUnits = SoftwareUnitsOf(plc)
            };
        }

        /// <summary>
        /// One entry per existing software unit, empty when the PLC has none.
        ///
        /// Only the S7-1500 family exposes software units: on an S7-1200 the provider
        /// service is simply not there and GetService returns null, which is a normal
        /// outcome and not a failure.
        ///
        /// A unit exposes the very same system-group types as the PLC itself, so the
        /// overloads below take it without a single new one.
        /// </summary>
        private static List<HierarchySoftwareUnitTargets> SoftwareUnitsOf(PlcSoftware plc)
        {
            List<HierarchySoftwareUnitTargets> targets = new List<HierarchySoftwareUnitTargets>();

            PlcUnitComposition units = Guard(() => plc.GetService<PlcUnitProvider>()?.UnitGroup?.Units);
            if (units == null) return targets;

            foreach (PlcUnit unit in units)
            {
                if (unit == null) continue;

                targets.Add(new HierarchySoftwareUnitTargets
                {
                    Name = unit.Name,
                    Blocks = From(unit.BlockGroup?.Groups),
                    TagTables = From(unit.TagTableGroup?.Groups),
                    Types = From(unit.TypeGroup?.Groups)
                });
            }

            return targets;
        }

        // One overload per family. Each resolves the child composition so the walk can
        // keep recursing without knowing which family it is in. Failures collapse to
        // null: creating a group is not worth taking TIA Portal down for.
        private static IGroupNode From(PlcBlockUserGroupComposition groups) =>
            groups == null ? null : new TiaGroupNode(name => Guard(() => From((groups.Find(name) ?? groups.Create(name)).Groups)));

        private static IGroupNode From(TechnologicalInstanceDBUserGroupComposition groups) =>
            groups == null ? null : new TiaGroupNode(name => Guard(() => From((groups.Find(name) ?? groups.Create(name)).Groups)));

        private static IGroupNode From(PlcTagTableUserGroupComposition groups) =>
            groups == null ? null : new TiaGroupNode(name => Guard(() => From((groups.Find(name) ?? groups.Create(name)).Groups)));

        private static IGroupNode From(PlcTypeUserGroupComposition groups) =>
            groups == null ? null : new TiaGroupNode(name => Guard(() => From((groups.Find(name) ?? groups.Create(name)).Groups)));

        private static T Guard<T>(Func<T> resolve) where T : class
        {
            try { return resolve(); }
            catch { return null; }
        }
    }
}
