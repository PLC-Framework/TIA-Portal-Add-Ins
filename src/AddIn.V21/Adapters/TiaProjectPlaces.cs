using System;
using System.Collections.Generic;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace AddIn.Adapters
{
    /// <summary>
    /// Where things are in a project: which PLCs it holds, and where an object sits inside one.
    ///
    /// **Shared by everything that walks the project**, because the answer has to be the same
    /// for all of them. The coding-style report and the export tree both say where an object
    /// lives - one in three columns, the other as folders on disk - and two copies of this
    /// walk would drift the first time one of them learned about a new kind of folder.
    ///
    /// Identical in V20 and V21 and duplicated for the usual reason: it touches Siemens types,
    /// and the two Add-In assemblies have different identities.
    /// </summary>
    internal static class TiaProjectPlaces
    {
        public const string Separator = "/";

        // Deep enough for any tree TIA lets an engineer build, and a guarantee that a Parent
        // chain which somehow loops cannot hold TIA's thread forever.
        private const int MaxParentSteps = 64;

        /// <summary>
        /// Where something sits: its PLC, its software unit - empty for the general program -
        /// and the folders under one of those two.
        ///
        /// The three are kept apart rather than joined into a path because both readers need
        /// them apart: the report shows them as columns, and the export makes folders of them.
        /// A project holds several PLCs and a PLC several units, each repeating the same folder
        /// names, so a single string could be filtered on as a whole or not at all.
        /// </summary>
        public struct Location
        {
            private Location(string plc, string unit, string folders)
            {
                Plc = plc;
                Unit = unit;
                Folders = folders;
            }

            public readonly string Plc;
            public readonly string Unit;
            public readonly string Folders;

            public static Location In(PlcSoftware plc) =>
                new Location(plc?.Name ?? string.Empty, string.Empty, string.Empty);

            /// <summary>The same PLC, inside one of its units, back at the root of its folders.</summary>
            public Location InUnit(string unit) => new Location(Plc, unit ?? string.Empty, string.Empty);

            public Location InFolder(string name) =>
                string.IsNullOrEmpty(name) ? this : new Location(Plc, Unit, Join(Folders, name));

            public static Location Of(string plc, string unit, IEnumerable<string> folders) =>
                new Location(plc ?? string.Empty, unit ?? string.Empty, string.Join(Separator, folders));
        }

        /// <summary>
        /// Where whatever sits under <paramref name="start"/> lives, built upwards: folder
        /// names, then the software unit if there is one, then the PLC. Links it does not
        /// know - the unit folder between a unit and its PLC, say - are stepped over rather
        /// than ending the walk, since which ones TIA puts in the chain is not something to
        /// bet on.
        /// </summary>
        public static Location LocationOf(IEngineeringObject start)
        {
            List<string> folders = new List<string>();
            string unit = null;
            string plc = null;

            IEngineeringObject current = start;
            for (int step = 0; current != null && step < MaxParentSteps; step++)
            {
                PlcSoftware software = current as PlcSoftware;
                if (software != null)
                {
                    plc = software.Name;
                    break;
                }

                // Hardware or the project: the software was left behind, so stop here.
                if (current is HardwareObject || current is Project) break;

                // A unit ends the folders and starts the climb towards its PLC: everything
                // above it belongs to the PLC, not to the folders of this object.
                PlcUnitBase unitLink = current as PlcUnitBase;
                if (unitLink != null) unit = unitLink.Name;
                else
                {
                    string name = FolderName(current);
                    if (name != null) folders.Add(name);
                }

                current = current.Parent;
            }

            folders.Reverse();
            return Location.Of(plc, unit, folders);
        }

        /// <summary>
        /// The PLC something belongs to, climbing through Parent, or null when the chain
        /// leaves the software behind.
        ///
        /// A software unit's objects answer with the PLC, not the unit: a unit is part of one
        /// PLC's software, and the services that act on a whole PLC - exporting its alarm text
        /// lists, for one - hang off that.
        /// </summary>
        public static PlcSoftware SoftwareFor(IEngineeringObject start)
        {
            IEngineeringObject current = start;

            for (int step = 0; current != null && step < MaxParentSteps; step++)
            {
                PlcSoftware software = current as PlcSoftware;
                if (software != null) return software;

                if (current is HardwareObject || current is Project) break;

                current = current.Parent;
            }

            return null;
        }

        /// <summary>
        /// The external source group that generates a source for this object: a software
        /// unit's own when it lives in one, otherwise its PLC's.
        ///
        /// **The unit's, not the PLC's, when there is one.** A unit is a compilation scope of
        /// its own, and generating one of its blocks through the PLC's group is asking the
        /// wrong half of the software for it.
        /// </summary>
        public static PlcExternalSourceSystemGroup SourcesFor(IEngineeringObject start)
        {
            IEngineeringObject current = start;

            for (int step = 0; current != null && step < MaxParentSteps; step++)
            {
                PlcUnitBase unit = current as PlcUnitBase;
                if (unit != null) return unit.ExternalSourceGroup;

                PlcSoftware software = current as PlcSoftware;
                if (software != null) return software.ExternalSourceGroup;

                if (current is HardwareObject || current is Project) break;

                current = current.Parent;
            }

            return null;
        }

        /// <summary>The name a link contributes to a path, or null for one that contributes none.</summary>
        public static string FolderName(IEngineeringObject link)
        {
            if (link is PlcBlockGroup) return ((PlcBlockGroup)link).Name;
            if (link is PlcTagTableGroup) return ((PlcTagTableGroup)link).Name;
            if (link is PlcTypeGroup) return ((PlcTypeGroup)link).Name;
            if (link is TechnologicalInstanceDBGroup) return ((TechnologicalInstanceDBGroup)link).Name;

            return null;
        }

        public static string Join(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent)) return name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return parent;

            return parent + Separator + name;
        }

        /// <summary>
        /// A project's PLCs, from the top-level devices and from every device folder. A set,
        /// because a device reachable two ways must still be reached once.
        /// </summary>
        public static IEnumerable<PlcSoftware> PlcsOf(Project project)
        {
            List<PlcSoftware> plcs = new List<PlcSoftware>();
            HashSet<PlcSoftware> seen = new HashSet<PlcSoftware>();

            Devices(project.Devices, plcs, seen);
            Devices(project.UngroupedDevicesGroup?.Devices, plcs, seen);

            foreach (DeviceUserGroup group in project.DeviceGroups) DeviceGroup(group, plcs, seen);

            return plcs;
        }

        /// <summary>
        /// The PLC a device item carries, or null. Asked of every module in a device, and
        /// most of them are not a CPU - a refusal there is an answer, not a failure.
        /// </summary>
        public static PlcSoftware SoftwareOf(DeviceItem item)
        {
            if (item == null) return null;

            try
            {
                return item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Units and safety units alike: both hold names an engineer chose.
        ///
        /// Only the S7-1500 family has software units. On an S7-1200 the provider service is
        /// not there, which is a normal answer and not a failure - the one place a missing
        /// piece of the tree is expected rather than suspicious.
        /// </summary>
        public static IEnumerable<PlcUnitBase> UnitsOf(PlcSoftware plc)
        {
            PlcUnitSystemGroup unitGroup;
            try
            {
                unitGroup = plc.GetService<PlcUnitProvider>()?.UnitGroup;
            }
            catch (Exception)
            {
                unitGroup = null;
            }

            List<PlcUnitBase> units = new List<PlcUnitBase>();
            if (unitGroup == null) return units;

            foreach (PlcUnit unit in unitGroup.Units) units.Add(unit);
            foreach (PlcSafetyUnit unit in unitGroup.SafetyUnits) units.Add(unit);

            return units;
        }

        private static void DeviceGroup(DeviceUserGroup group, List<PlcSoftware> plcs, HashSet<PlcSoftware> seen)
        {
            if (group == null) return;

            Devices(group.Devices, plcs, seen);
            foreach (DeviceUserGroup child in group.Groups) DeviceGroup(child, plcs, seen);
        }

        private static void Devices(DeviceComposition devices, List<PlcSoftware> plcs, HashSet<PlcSoftware> seen)
        {
            if (devices == null) return;

            foreach (Device device in devices)
            {
                if (device == null) continue;
                foreach (DeviceItem item in device.DeviceItems) DeviceItems(item, plcs, seen);
            }
        }

        private static void DeviceItems(DeviceItem item, List<PlcSoftware> plcs, HashSet<PlcSoftware> seen)
        {
            if (item == null) return;

            PlcSoftware plc = SoftwareOf(item);
            if (plc != null && seen.Add(plc)) plcs.Add(plc);

            foreach (DeviceItem child in item.DeviceItems) DeviceItems(child, plcs, seen);
        }
    }
}
