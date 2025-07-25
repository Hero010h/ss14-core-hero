using Content.Shared.Administration;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Sandboxing;
using Robust.Client.UserInterface;
using System.Linq;

namespace Content.Client.UserInterface.Controls;

// mfw they ported input() from BYOND

/// <summary>
/// Client-side dialog with multiple prompts.
/// Used by admin tools quick dialog system among other things.
/// </summary>
[GenerateTypedNameReferences]
public sealed partial class DialogWindow : FancyWindow
{
    /// <summary>
    /// Action for when the ok button is pressed or the last field has enter pressed.
    /// Results maps prompt FieldIds to the LineEdit's text contents.
    /// </summary>
    public Action<Dictionary<string, string>>? OnConfirmed;

    /// <summary>
    /// Action for when the cancel button is pressed or the window is closed.
    /// </summary>
    public Action? OnCancelled;

    /// <summary>
    /// Used to ensure that only one output action is invoked.
    /// E.g. Pressing cancel will invoke then close the window, but OnClose will not invoke.
    /// </summary>
    private bool _finished;

    private List<(string, Func<string>)> _promptLines;

    private delegate (Control, Func<string>) ControlConstructor(Func<string, bool> verifier, string name, QuickDialogEntry entry, bool last, object? value);// are you feeling it now mr krabs?

    /// <summary>
    /// Create and open a new dialog with some prompts.
    /// </summary>
    /// <param name="title">String to use for the window title.</param>
    /// <param name="entries">Quick dialog entries to create prompts with.</param>
    /// <param name="ok">Whether to have an Ok button.</param>
    /// <param name="cancel">Whether to have a Cancel button. Closing the window will still cancel it.</param>
    /// <remarks>
    /// Won't do anything on its own, you need to handle or network with <see cref="OnConfirmed"/> and <see cref="OnCancelled"/>.
    /// </remarks>
    public DialogWindow(string title, List<QuickDialogEntry> entries, bool ok = true, bool cancel = true)
    {
        RobustXamlLoader.Load(this);

        Title = title;

        OkButton.Visible = ok;
        CancelButton.Visible = cancel;

        _promptLines = new(entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var value = entry.Value;
            var box = new BoxContainer();
            box.AddChild(new Label() { Text = entry.Prompt, HorizontalExpand = true, SizeFlagsStretchRatio = 0.5f });

            //var edit = new LineEdit() { HorizontalExpand = true };

            
            //  walkthrough:
            //  second item in this tuple is a verifier function for controls who have that option
            //  third item is a backup name in case we have not been provided with one from the server
            //  first item is a function that takes the other two, the QuickDialogEntry for it, a bool of whether it's the last control in the window, the default value for the control
            // and returns another tuple:
            //      item 1 is the control itself
            //      item 2 is a Func<string>, a """generic""" function that returns whatever user has done with the control
            (ControlConstructor, Func<string, bool>?, string) notapairanymore = entry.Type switch 
            {
                QuickDialogEntryType.Integer => (SetupLineEditNumber, VerifyInt, "integer"),
                QuickDialogEntryType.Float => (SetupLineEditNumber, VerifyFloat, "float"),
                QuickDialogEntryType.ShortText => (SetupLineEdit, VerifyShortText, "short-text"),
                QuickDialogEntryType.LongText => (SetupLineEdit, VerifyLongText, "long-text"),
                QuickDialogEntryType.Hex16 => (SetupLineEditHex, VerifyHex16, "hex16"),
                QuickDialogEntryType.Boolean => (SetupCheckBox, null, "boolean"),
                QuickDialogEntryType.Void => (SetupVoid, null, "void"),

                _ => throw new ArgumentOutOfRangeException()
            };
            var (setup, valid, name) = notapairanymore;

            // try use placeholder from the caller, fall back to the generic one for whatever type is being validated.
            var (control, returner) = setup(valid!, entry.Placeholder ?? Loc.GetString($"quick-dialog-ui-{name}"), entry, i == entries.Count - 1, value);   // ARE YOU FEELING IT NOW MR KRABS?
                                                                                                                                                            // yes, valid can be null
                                                                                                                                                            // yes, i am just going to ignore that
                                                                                                                                                            // go fuck yourself
            _promptLines.Add((entry.FieldId, returner));

            box.AddChild(control);
            Prompts.AddChild(box);
        }

        OkButton.OnPressed += _ => Confirm();

        CancelButton.OnPressed += _ =>
        {
            _finished = true;
            OnCancelled?.Invoke();
            Close();
        };

        OnClose += () =>
        {
            if (!_finished)
                OnCancelled?.Invoke();
        };

        MinWidth *= 2; // Just double it.

        OpenCentered();
    }

    private void Confirm()
    {
        var results = new Dictionary<string, string>();
        foreach (var (field, returner) in _promptLines)
        {
            results[field] = returner();
        }

        _finished = true;
        OnConfirmed?.Invoke(results);
        Close();
    }

    #region Input validation


    private bool VerifyInt(string input)
    {
        return int.TryParse(input, out var _);
    }

    private bool VerifyFloat(string input)
    {
        return float.TryParse(input, out var _);
    }

    private bool VerifyShortText(string input)
    {
        return input.Length <= 100;
    }

    private bool VerifyLongText(string input)
    {
        return input.Length <= 2000;
    }

    private bool VerifyHex16(string input)
    {
        return input.Length <= 4 && int.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out int _);
    }




    private (Control, Func<string>) SetupLineEdit(Func<string, bool> valid, string name, QuickDialogEntry entry, bool last, object? value) // oh shit i'm feeling it
    {
        var edit = new LineEdit() { HorizontalExpand = true };
        edit.IsValid += valid;
        if(value != null)
            edit.Text = value.ToString() ?? "";
        edit.PlaceHolder = name;

        // Last text box gets enter confirmation.
        // Only the last so you don't accidentally confirm early.
        if (last)
            edit.OnTextEntered += _ => Confirm();

        return (edit, () => {return edit.Text;} );
    }
    private (Control, Func<string>) SetupLineEditNumber(Func<string, bool> valid, string name, QuickDialogEntry entry, bool last, object? value)
    {
        var (control, returner) = SetupLineEdit(valid, name, entry, last, value);
        var le = (LineEdit) control;
        return (control, ()=> le.Text.Length > 0 ? le.Text : "0"); // Otherwise you'll get kicked for malformed data
    }
    private (Control, Func<string>) SetupLineEditHex(Func<string, bool> valid, string name, QuickDialogEntry entry, bool last, object? value)
    {
        var (control, returner) = SetupLineEditNumber(valid, name, entry, last, value);
        var le = (LineEdit) control;
        if(value is int)
        {
            le.Text = ((int)value).ToString("X");
        }
        le.OnTextChanged += e => { e.Control.Text = e.Text.ToUpper(); };
        return (control, returner);
    }

    private (Control, Func<string>) SetupCheckBox(Func<string, bool> _, string name, QuickDialogEntry entry, bool last, object? value)
    {
        var check = new CheckBox() { HorizontalExpand = true, HorizontalAlignment = HAlignment.Right };
        check.Text = name;
        value ??= false;
        check.Pressed = (bool)value;
        return (check, () => { return check.Pressed ? "true" : "false"; });
    }

    private (Control, Func<string>) SetupVoid(Func<string, bool> _, string __, QuickDialogEntry ___, bool ____, object? _____)
    {
        var control = new Control();
        control.Visible = false;
        return (control, () => "" );
    }



    #endregion
}
