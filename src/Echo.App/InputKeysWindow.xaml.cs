using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Echo.Core;

namespace Echo.App;

public partial class InputKeysWindow : Window
{
    private InputOverlayPreset _preset;
    private readonly Action<InputOverlayPreset> _changed;
    private readonly Dictionary<string,ToggleButton> _keys = new(StringComparer.Ordinal);
    private bool _updating;

    public InputKeysWindow(InputOverlayPreset preset,Action<InputOverlayPreset> changed)
    {
        _preset=preset;_changed=changed;InitializeComponent();
        for (int index = 0; index < OverlayAppearance.KeyboardRows.Count; index++)
        {
            var heading = index switch { 0 => "Keyboard", 6 => "Navigation and system", 7 => "Number pad", _ => null };
            if (heading != null)
                KeyRowsPanel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 6) });
            var row = OverlayAppearance.KeyboardRows[index];
            var panel = new WrapPanel();
            KeyRowsPanel.Children.Add(panel);
            foreach(var id in row)
            {
                var key=new ToggleButton{Content=OverlayAppearance.KeyLabel(id),Tag=id,Style=(Style)FindResource("KeyChoice"),ToolTip=id};
                System.Windows.Automation.AutomationProperties.SetName(key,id);
                key.Checked+=KeyChanged;key.Unchecked+=KeyChanged;_keys.Add(id,key);panel.Children.Add(key);
            }
        }
        BindFields();
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
    }
    private void BindFields()
    {
        _updating=true;
        var selected=_preset.Layout=="Custom"?_preset.Keys:OverlayAppearance.Layout(_preset).Keys.Where(k=>OverlayAppearance.KeyboardKeys.Contains(k.Id)).Select(k=>k.Id).ToArray();
        foreach(var pair in _keys)pair.Value.IsChecked=selected.Contains(pair.Key,StringComparer.Ordinal);
        MouseChoice.IsChecked=_preset.Layout!="Custom"||_preset.ShowMouse;_updating=false;RefreshPreview();
    }
    private void Commit()
    {
        if(_updating)return;
        _preset=_preset with{Layout="Custom",Keys=OverlayAppearance.KeyboardRows.SelectMany(r=>r).Where(k=>_keys[k].IsChecked==true).ToArray(),ShowMouse=MouseChoice.IsChecked==true};
        _preset.Validate();RefreshPreview();_changed(_preset);
    }
    private void RefreshPreview()
    {
        KeyPreview.Preset=_preset with{Layout="Custom",Keys=_keys.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToArray(),ShowMouse=MouseChoice.IsChecked==true,Collapsed=false,ShowApm=false};
        KeyPreview.Height=KeyPreview.Width/KeyPreview.Aspect;KeyPreview.InvalidateVisual();
    }
    private void KeyChanged(object sender,RoutedEventArgs e)=>Commit();
    private void MouseChanged(object sender,RoutedEventArgs e)=>Commit();
    private void LeaguePresetClick(object sender,RoutedEventArgs e){_preset=_preset with{Layout="Custom",Keys=new InputOverlayPreset().Keys};BindFields();Commit();}
    private void SelectAllClick(object sender,RoutedEventArgs e){_preset=_preset with{Layout="Custom",Keys=OverlayAppearance.KeyboardRows.SelectMany(row=>row).ToArray()};BindFields();Commit();}
    private void ClearKeysClick(object sender,RoutedEventArgs e){_preset=_preset with{Layout="Custom",Keys=[]};BindFields();Commit();}
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private void WindowKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}}
    private void DragHeader(object sender,MouseButtonEventArgs e){if(e.OriginalSource is TextBlock&&e.LeftButton==MouseButtonState.Pressed)DragMove();}
    internal void SelectKey(string key,bool selected)=>_keys[key].IsChecked=selected;
    internal void SelectMouse(bool selected)=>MouseChoice.IsChecked=selected;
    internal InputOverlayPreset Preset=>_preset;
}
