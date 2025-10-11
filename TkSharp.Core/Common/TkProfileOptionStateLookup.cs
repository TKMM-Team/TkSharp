using TkSharp.Core.Models;

namespace TkSharp.Core.Common;

public class TkProfileOptionStateLookup(TkModOption target, TkModOptionGroup group, TkProfileMod parent)
{
    public bool GetIsEnabled()
    {
        return parent.SelectedOptions.TryGetValue(group, out var selected)
               && selected.Contains(target);
    }

    public void SetIsEnabled(bool value)
    {
        if (!parent.SelectedOptions.TryGetValue(group, out var selected)) {
            if (!value) {
                return;
            }
            
            parent.SelectedOptions[group] = selected = [];
        }

        switch (value) {
            case true:
                switch (group.Type) {
                    case OptionGroupType.Single or OptionGroupType.SingleRequired:
                        PurgeAfterUpdate(selected, target, selected.ToArray());
                        UpdateState();
                        return;
                    case OptionGroupType.MultiRequired:
                        selected.Add(target);
                        UpdateState();
                        return;
                }

                selected.Add(target);
                break;
            case false:
                selected.Remove(target);

                if (group.Type is OptionGroupType.MultiRequired or OptionGroupType.SingleRequired) {
                    UpdateState();
                }
                break;
        }
    }

    private static void PurgeAfterUpdate(HashSet<TkModOption> selected, TkModOption target, TkModOption[] purge)
    {
        selected.Add(target);
        
        foreach (var option in purge) {
            option.IsEnabled = false;
        }
    }

    private void UpdateState()
    {
        foreach (var option in group.Options) {
            option.UpdateState();
        }
    }

    public bool CanChangeState()
    {
        return !(
            parent.SelectedOptions.TryGetValue(group, out var selected)
               && selected.Contains(target)
               && selected.Count == 1
               && group.Type is OptionGroupType.MultiRequired or OptionGroupType.SingleRequired
        );
    }
}