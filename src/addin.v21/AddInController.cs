using System.Linq;

using Siemens.Engineering;
using Siemens.Engineering.AddIn;
using Siemens.Engineering.AddIn.Menu;

namespace addin
{
    public class AddInController : ContextMenuAddIn
    {
        private const string Title = "PLC-Framework";
        private readonly TiaPortal _tiaPortal;

        public AddInController(TiaPortal tiaPortal) : base(Title)
        {
            _tiaPortal = tiaPortal;
        }

        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {
            addInRootSubmenu.Items.AddActionItem<Project> (
                "Hello world!",
                (MenuSelectionProvider<Project> menuSelectionProvider) =>
                {
                    Project project = menuSelectionProvider?.GetSelection<Project>().FirstOrDefault();
                    string projectName = project?.Name ?? "No project";

                    _tiaPortal.GetService<MessageBoxProvider>()
                        .ShowNotification(NotificationIcon.Information, Title, $"Hello world! {projectName}");

                });
        }
    }
}
