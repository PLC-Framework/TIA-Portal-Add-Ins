using System;
using System.Collections.Generic;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW.Blocks;

using AddIn.Shared.Adapters;

namespace AddIn.Adapters
{
    /// <summary>
    /// Turns a selection of data blocks into the primitives the satellite needs.
    ///
    /// Identical in V20 and V21 and duplicated for the usual reason: it touches Siemens
    /// types, and the two Add-In assemblies have different identities.
    /// </summary>
    internal static class TiaPlcSelection
    {
        public static PlcSelection From(IEnumerable<DataBlock> blocks)
        {
            List<string> names = new List<string>();
            DeviceItem owner = null;

            foreach (DataBlock block in blocks)
            {
                if (block == null) continue;

                names.Add(block.Name);

                // Every selected block belongs to the same CPU - one capture, one PLC -
                // so the first one that resolves settles it.
                if (owner == null) owner = OwningDeviceItem(block);
            }

            Device device = DeviceOf(owner);

            return new PlcSelection(device?.Name ?? owner?.Name, Addresses(device), names);
        }

        /// <summary>
        /// Walk up until a DeviceItem appears. The chain from a block runs through its
        /// group, the software, and the software container before reaching hardware, and
        /// none of those steps is worth naming individually.
        /// </summary>
        private static DeviceItem OwningDeviceItem(IEngineeringObject start)
        {
            for (IEngineeringObject current = start; current != null; current = current.Parent)
            {
                DeviceItem item = current as DeviceItem;
                if (item != null) return item;
            }

            return null;
        }

        private static Device DeviceOf(IEngineeringObject start)
        {
            for (IEngineeringObject current = start; current != null; current = current.Parent)
            {
                Device device = current as Device;
                if (device != null) return device;
            }

            return null;
        }

        private static IReadOnlyList<string> Addresses(Device device)
        {
            List<string> found = new List<string>();
            if (device == null) return found;

            foreach (DeviceItem item in device.DeviceItems) Collect(item, found);

            return found;
        }

        /// <summary>
        /// The whole device is walked, not just the item that owns the software: the
        /// interface is a separate child item - "PROFINET interface_1" and the like - so
        /// asking the CPU item alone finds nothing.
        /// </summary>
        private static void Collect(DeviceItem item, List<string> found)
        {
            if (item == null) return;

            NetworkInterface network = null;
            try
            {
                network = item.GetService<NetworkInterface>();
            }
            catch (Exception)
            {
                // An item that cannot answer is simply not an interface.
            }

            if (network != null)
            {
                foreach (Node node in network.Nodes)
                {
                    string address = AddressOf(node);

                    if (LooksLikeIp(address) && !found.Contains(address))
                        found.Add(address);
                }
            }

            foreach (DeviceItem child in item.DeviceItems) Collect(child, found);
        }

        /// <summary>
        /// The IP is an attribute of the node, not a typed property.
        ///
        /// HW.Address is a different thing entirely - an I/O address, with a start and a
        /// length - and reaching for it here is the obvious wrong turn. The attribute is
        /// normally called "Address"; when a node calls it something else, its own
        /// attribute list is asked rather than guessing a second name.
        /// </summary>
        private static string AddressOf(Node node)
        {
            try
            {
                return node.GetAttribute("Address")?.ToString();
            }
            catch (Exception)
            {
            }

            try
            {
                foreach (EngineeringAttributeInfo info in node.GetAttributeInfos())
                {
                    if (info.Name.IndexOf("address", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string value = node.GetAttribute(info.Name)?.ToString();
                    if (LooksLikeIp(value)) return value;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// A PROFIBUS node also has an address, and it is a number like "2". Offering
        /// that in a list of things to connect to over HTTP would only waste the
        /// operator's time.
        /// </summary>
        private static bool LooksLikeIp(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string[] parts = value.Trim().Split('.');
            if (parts.Length != 4) return false;

            foreach (string part in parts)
            {
                byte number;
                if (!byte.TryParse(part, out number)) return false;
            }

            return true;
        }
    }
}
