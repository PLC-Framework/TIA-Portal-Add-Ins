using System;

using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;

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
        /// The four group roots of the selected device, or null when it is not a PLC.
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
                Types = From(plc.TypeGroup?.Groups)
            };
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

        private static IGroupNode Guard(Func<IGroupNode> create)
        {
            try { return create(); }
            catch { return null; }
        }
    }
}
