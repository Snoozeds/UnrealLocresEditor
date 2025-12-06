using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Splat.ModeDetection;
using UnrealLocresEditor.Models;

namespace UnrealLocresEditor.Views
{
    public partial class UnsavedChanges : Window
    {
        public UnsavedChangesResult Result { get; private set; }
        public enum UnsavedChangesMode
        {
            Closing,
            Merging
        }

        public UnsavedChanges(UnsavedChangesMode mode)
        {
            InitializeComponent();
            SetDialogText(mode);

            this.FindControl<Button>("YesButton")!.Click += (_, __) =>
            {
                Result = UnsavedChangesResult.Yes;
                Close(Result);
            };

            this.FindControl<Button>("NoButton")!.Click += (_, __) =>
            {
                Result = UnsavedChangesResult.No;
                Close(Result);
            };

            this.FindControl<Button>("CancelButton")!.Click += (_, __) =>
            {
                Result = UnsavedChangesResult.Cancel;
                Close(Result);
            };
        }

        private void SetDialogText(UnsavedChangesMode mode)
        {
            var textBlock = this.FindControl<TextBlock>("MessageText");

            if (mode == UnsavedChangesMode.Merging)
            {
                textBlock!.Text = "You have unsaved changes.\nDo you want to save before merging?";
            }
            else
            {
                textBlock!.Text = "You have unsaved changes.\nDo you want to save before closing?";
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
