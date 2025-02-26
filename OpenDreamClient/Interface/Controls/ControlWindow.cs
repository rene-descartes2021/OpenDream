﻿using OpenDreamClient.Input;
using OpenDreamClient.Interface.Descriptors;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace OpenDreamClient.Interface.Controls
{
    public sealed class ControlWindow : InterfaceControl
    {
        [Dependency] private readonly IUserInterfaceManager _uiMgr = default!;
        [Dependency] private readonly IDreamInterfaceManager _dreamInterface = default!;

        // NOTE: a "window" in BYOND does not necessarily map 1:1 to OS windows.
        // Just like in win32 (which is definitely what this is inspired by let's be real),
        // windows can be embedded into other windows as a way to do nesting.

        public List<InterfaceControl> ChildControls = new();

        private readonly WindowDescriptor _windowDescriptor;
        private MenuBar _menu = default!;
        private LayoutContainer _canvas = default!;
        private readonly List<(OSWindow osWindow, IClydeWindow clydeWindow)> _openWindows = new();

        public ControlWindow(WindowDescriptor windowDescriptor) : base(windowDescriptor.MainControlDescriptor, null)
        {
            IoCManager.InjectDependencies(this);

            _windowDescriptor = windowDescriptor;
        }

        public override void UpdateElementDescriptor()
        {
            // Don't call base.UpdateElementDescriptor();

            var controlDescriptor = (ControlDescriptorMain)ElementDescriptor;

            if (controlDescriptor.Menu != null)
            {
                _menu.Visible = true;

                InterfaceDescriptor interfaceDescriptor = _dreamInterface.InterfaceDescriptor;

                interfaceDescriptor.MenuDescriptors.TryGetValue(controlDescriptor.Menu,
                    out MenuDescriptor menuDescriptor);
                CreateMenu(menuDescriptor);
            }
            else
            {
                _menu.Visible = false;
            }

            foreach (var window in _openWindows)
            {
                UpdateWindowAttributes(window, controlDescriptor);
            }
        }

        public OSWindow CreateWindow()
        {
            OSWindow window = new();

            window.Children.Add(UIElement);
            window.SetWidth = _controlDescriptor.Size?.X ?? 640;
            window.SetHeight = _controlDescriptor.Size?.Y ?? 440;
            if(_controlDescriptor.Size?.X == 0)
                window.SetWidth = window.MaxWidth;
            if(_controlDescriptor.Size?.Y == 0)
                window.SetHeight = window.MaxHeight;
            window.Closing += _ => { _openWindows.Remove((window, null)); };

            _openWindows.Add((window, null));
            UpdateWindowAttributes((window, null), (ControlDescriptorMain)ElementDescriptor);
            return window;
        }

        public void RegisterOnClydeWindow(IClydeWindow window)
        {
            // todo: listen for closed.
            _openWindows.Add((null, window));
            UpdateWindowAttributes((null, window), (ControlDescriptorMain)ElementDescriptor);
        }

        public void UpdateAnchors()
        {
            var windowSize = Size.GetValueOrDefault();
            if(windowSize.X == 0)
                windowSize.X = 640;
            if(windowSize.Y == 0)
                windowSize.Y = 440;

            for(int i = 0; i < ChildControls.Count; i++)
            {
                InterfaceControl control = ChildControls[i];
                var element = control.UIElement;
                var elementPos = control.Pos.GetValueOrDefault();
                var elementSize = control.Size.GetValueOrDefault();

                if(control.Size?.Y == 0)
                {
                    elementSize.Y = (int) (windowSize.Y - elementPos.Y);
                    if(ChildControls.Count - 1 > i)
                    {
                        if(ChildControls[i+1].Pos != null)
                        {
                            var nextElementPos = ChildControls[i+1].Pos.GetValueOrDefault();
                            elementSize.Y = nextElementPos.Y - elementPos.Y;
                        }
                    }
                    element.SetHeight = (elementSize.Y/windowSize.Y) * _canvas.Height;
                }
                if(control.Size?.X == 0)
                {
                    elementSize.X = (int) (windowSize.X - elementPos.X);
                    if(ChildControls.Count - 1 > i)
                    {
                        if(ChildControls[i+1].Pos != null)
                        {
                            var nextElementPos = ChildControls[i+1].Pos.GetValueOrDefault();
                            if(nextElementPos.X < (elementSize.X + elementPos.X) && nextElementPos.Y < (elementSize.Y + elementPos.Y))
                                elementSize.X = nextElementPos.X - elementPos.X;
                        }
                    }
                    element.SetWidth = (elementSize.X/windowSize.X) * _canvas.Width;
                }

                if (control.Anchor1.HasValue)
                {
                    var offset1X = elementPos.X - (windowSize.X * control.Anchor1.Value.X / 100f);
                    var offset1Y = elementPos.Y - (windowSize.Y * control.Anchor1.Value.Y / 100f);
                    var left = (_canvas.Width * control.Anchor1.Value.X / 100) + offset1X;
                    var top = (_canvas.Height * control.Anchor1.Value.Y / 100) + offset1Y;
                    LayoutContainer.SetMarginLeft(element, Math.Max(left, 0));
                    LayoutContainer.SetMarginTop(element, Math.Max(top, 0));

                    if (control.Anchor2.HasValue)
                    {
                        if(control.Anchor2.Value.X < control.Anchor1.Value.X || control.Anchor2.Value.Y < control.Anchor1.Value.Y)
                            Logger.Warning($"Invalid anchor2 value in DMF for element {control.Name}. Ignoring.");
                        else
                        {
                            var offset2X = (elementPos.X + elementSize.X) -
                                        (windowSize.X * control.Anchor2.Value.X / 100);
                            var offset2Y = (elementPos.Y + elementSize.Y) -
                                        (windowSize.Y * control.Anchor2.Value.Y / 100);
                            var width = (_canvas.Width * control.Anchor2.Value.X / 100) + offset2X - left;
                            var height = (_canvas.Height * control.Anchor2.Value.Y / 100) + offset2Y - top;
                            element.SetWidth = Math.Max(width, 0);
                            element.SetHeight = Math.Max(height, 0);
                        }

                    }
                }
            }
        }

        private void UpdateWindowAttributes(
            (OSWindow osWindow, IClydeWindow clydeWindow) windowRoot,
            ControlDescriptorMain descriptor)
        {
            // TODO: this would probably be cleaner if an OSWindow for MainWindow was available.
            var (osWindow, clydeWindow) = windowRoot;

            var title = descriptor.Title ?? "OpenDream World";
            if (osWindow != null) osWindow.Title = title;
            else if (clydeWindow != null) clydeWindow.Title = title;

            WindowRoot root = null;
            if (osWindow?.Window != null)
                root = _uiMgr.GetWindowRoot(osWindow.Window);
            else if (clydeWindow != null)
                root = _uiMgr.GetWindowRoot(clydeWindow);

            if (root != null)
            {
                root.BackgroundColor = descriptor.BackgroundColor;
            }
        }

        public void CreateChildControls(IDreamInterfaceManager manager)
        {
            foreach (ControlDescriptor controlDescriptor in _windowDescriptor.ControlDescriptors)
            {
                if (controlDescriptor == _windowDescriptor.MainControlDescriptor) continue;

                InterfaceControl control = controlDescriptor switch
                {
                    ControlDescriptorChild => new ControlChild(controlDescriptor, this),
                    ControlDescriptorInput => new ControlInput(controlDescriptor, this),
                    ControlDescriptorButton => new ControlButton(controlDescriptor, this),
                    ControlDescriptorOutput => new ControlOutput(controlDescriptor, this),
                    ControlDescriptorInfo => new ControlInfo(controlDescriptor, this),
                    ControlDescriptorMap => new ControlMap(controlDescriptor, this),
                    ControlDescriptorBrowser => new ControlBrowser(controlDescriptor, this),
                    ControlDescriptorLabel => new ControlLabel(controlDescriptor, this),
                    ControlDescriptorGrid => new ControlGrid(controlDescriptor, this),
                    ControlDescriptorTab => new ControlTab(controlDescriptor, this),
                    _ => throw new Exception($"Invalid descriptor {controlDescriptor.GetType()}")
                };
                // Can't have out-of-order components, so make sure they're ordered properly
                if(ChildControls.Count > 0) {
                    var prevPos = ChildControls[ChildControls.Count-1].Pos.GetValueOrDefault();
                    var curPos = control.Pos.GetValueOrDefault();
                    if(prevPos.X <= curPos.X && prevPos.Y <= curPos.Y)
                        ChildControls.Add(control);
                    else {
                        Logger.Warning($"Out of order component {control.Name}. Elements should be defined in order of position. Attempting to fix automatically.");
                        int i = 0;
                        while(i < ChildControls.Count) {
                            prevPos = ChildControls[i].Pos.GetValueOrDefault();
                            if(prevPos.X <= curPos.X && prevPos.Y <= curPos.Y)
                                i++;
                            else
                                break;
                        }
                        ChildControls.Insert(i, control);
                    }
                } else
                    ChildControls.Add(control);

                _canvas.Children.Add(control.UIElement);
            }
        }

        // Because of how windows are not always real windows,
        // UIControl contains the *contents* of the window, not the actual OS window itself.
        protected override Control CreateUIElement()
        {
            var container = new BoxContainer
            {
                RectClipContent = true,
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Children =
                {
                    (_menu = new MenuBar { Margin = new Thickness(4, 0)}),
                    (_canvas = new LayoutContainer
                    {
                        InheritChildMeasure = false,
                        VerticalExpand = true
                    })
                }
            };

            _canvas.OnResized += CanvasOnResized;

            return container;
        }

        private void CanvasOnResized()
        {
            UpdateAnchors();
        }

        private class MenuNode {
            public MenuElementDescriptor Data;
            public List<MenuNode> Children = new();

            public MenuNode(MenuElementDescriptor myData) {
                this.Data = myData;
            }

            public MenuBar.MenuEntry GetMenuEntry() {
                string name = Data.Name;
                if (name.StartsWith("&"))
                    name = name[1..]; //TODO: First character in name becomes a selection shortcut

                if(Children.Count > 0) {
                    MenuBar.SubMenu result = new MenuBar.SubMenu();
                    result.Text = name;
                    foreach(MenuNode child in Children)
                        result.Entries.Add(child.GetMenuEntry());

                    return result;
                } else if(!String.IsNullOrEmpty(name)) {
                    MenuBar.MenuButton result = new MenuBar.MenuButton();
                    result.Text = name;
                    //result.IsCheckable = data.CanCheck;
                    if (!String.IsNullOrEmpty(Data.Command))
                        result.OnPressed += () => { EntitySystem.Get<DreamCommandSystem>().RunCommand(Data.Command); };
                    return result;
                } else
                    return new MenuBar.MenuSeparator();
            }
        }
        private void CreateMenu(MenuDescriptor menuDescriptor) {
            _menu.Menus.Clear();
            if (menuDescriptor == null) return;
            List<MenuNode> menuTree = new();
            Dictionary<string, MenuNode> treeQuickLookup = new();

            foreach (MenuElementDescriptor elementDescriptor in menuDescriptor.Elements) {
                if (elementDescriptor.Category == null) {
                    MenuNode topLevelMenuItem = new(elementDescriptor);
                    treeQuickLookup.Add(elementDescriptor.Name, topLevelMenuItem);
                    menuTree.Add(topLevelMenuItem);
                } else {
                    if (!treeQuickLookup.ContainsKey(elementDescriptor.Category)) {
                        //if category is set but the parent element doesn't exist, create it
                        MenuElementDescriptor parentMenuItem = new MenuElementDescriptor();
                        parentMenuItem.Name = elementDescriptor.Category;
                        MenuNode topLevelMenuItem = new(parentMenuItem);
                        treeQuickLookup.Add(parentMenuItem.Name, topLevelMenuItem);
                        menuTree.Add(topLevelMenuItem);
                    }
                    //now add this as a child
                    MenuNode childMenuItem = new MenuNode(elementDescriptor);
                    treeQuickLookup[elementDescriptor.Category].Children.Add(childMenuItem);
                    treeQuickLookup.Add(elementDescriptor.Name, childMenuItem);
                }
            }

            foreach (MenuNode topLevelMenuItem in menuTree) {
                MenuBar.Menu menu = new MenuBar.Menu();
                menu.Title = topLevelMenuItem.Data.Name;
                if (menu.Title?.StartsWith("&") ?? false)
                    menu.Title = menu.Title[1..]; //TODO: First character in name becomes a selection shortcut

                _menu.Menus.Add(menu);
                //visit each node in the tree, populating the menu from that
                foreach (MenuNode child in topLevelMenuItem.Children)
                    menu.Entries.Add(child.GetMenuEntry());
            }
        }
    }
}
