using OpenDreamRuntime.Procs;
using OpenDreamRuntime.Rendering;
using OpenDreamRuntime.Resources;
using OpenDreamShared.Dream;
using Robust.Shared.Utility;

namespace OpenDreamRuntime.Objects.MetaObjects {
    sealed class DreamMetaObjectAtom : IDreamMetaObject {
        public bool ShouldCallNew => true;
        public IDreamMetaObject? ParentType { get; set; }

        [Dependency] private readonly IDreamManager _dreamManager = default!;
        [Dependency] private readonly DreamResourceManager _resourceManager = default!;
        [Dependency] private readonly IDreamMapManager _mapManager = default!;
        [Dependency] private readonly IDreamObjectTree _objectTree = default!;
        [Dependency] private readonly IAtomManager _atomManager = default!;

        private readonly Dictionary<DreamObject, DreamFilterList> _filterLists = new();

        public DreamMetaObjectAtom() {
            IoCManager.InjectDependencies(this);
        }

        public void OnObjectCreated(DreamObject dreamObject, DreamProcArguments creationArguments) {
            // Turfs can be new()ed multiple times, so let DreamMapManager handle it.
            if (!dreamObject.IsSubtypeOf(_objectTree.Turf)) {
                _mapManager.AllAtoms.Add(dreamObject);
            }

            // TODO: Create a special list to prevent this needless list creation
            List<int>? objectVerbs = dreamObject.ObjectDefinition.Verbs;
            if (objectVerbs != null) {
                DreamList verbsList = DreamList.Create(objectVerbs.Count);

                foreach (int verbId in objectVerbs) {
                    DreamProc verb = _objectTree.Procs[verbId];

                    verbsList.AddValue(new DreamValue(verb));
                }

                dreamObject.SetVariableValue("verbs", new DreamValue(verbsList));
            }

            _filterLists[dreamObject] = new DreamFilterList(dreamObject);

            ParentType?.OnObjectCreated(dreamObject, creationArguments);
        }

        public void OnObjectDeleted(DreamObject dreamObject) {
            _filterLists.Remove(dreamObject);

            _atomManager.DeleteMovableEntity(dreamObject);

            // Replace our world.contents spot with the last so this doesn't mess with enumerators
            // Results in a different order than BYOND, but nothing about our order resembles BYOND at all right now
            // TODO: Handle this placing atoms earlier than an enumerator's index
            int worldContentsIndex = _mapManager.AllAtoms.IndexOf(dreamObject);
            _mapManager.AllAtoms.RemoveSwap(worldContentsIndex);

            _atomManager.OverlaysListToAtom.Remove(dreamObject.GetVariable("overlays").GetValueAsDreamList());
            _atomManager.UnderlaysListToAtom.Remove(dreamObject.GetVariable("underlays").GetValueAsDreamList());
            ParentType?.OnObjectDeleted(dreamObject);
        }

        public void OnVariableSet(DreamObject dreamObject, string varName, DreamValue value, DreamValue oldValue) {
            ParentType?.OnVariableSet(dreamObject, varName, value, oldValue);

            switch (varName) {
                case "icon":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        if (_resourceManager.TryLoadIcon(value, out var icon)) {
                            appearance.Icon = icon.Id;
                        } else {
                            appearance.Icon = null;
                        }
                    });
                    break;
                case "icon_state":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        value.TryGetValueAsString(out appearance.IconState);
                    });
                    break;
                case "pixel_x":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        value.TryGetValueAsInteger(out appearance.PixelOffset.X);
                    });
                    break;
                case "pixel_y":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        value.TryGetValueAsInteger(out appearance.PixelOffset.Y);
                    });
                    break;
                case "layer":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        value.TryGetValueAsFloat(out appearance.Layer);
                    });
                    break;
                case "invisibility":
                    value.TryGetValueAsInteger(out int vis);
                    vis = Math.Clamp(vis, -127, 127); // DM ref says [0, 101]. BYOND compiler says [-127, 127]
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        appearance.Invisibility = vis;
                    });
                    dreamObject.SetVariableValue("invisibility", new DreamValue(vis));
                    break;
                case "opacity":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        value.TryGetValueAsInteger(out var opacity);
                        appearance.Opacity = (opacity != 0);
                    });
                    break;
                case "mouse_opacity":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        //TODO figure out the weird inconsistencies with this being internally clamped
                        value.TryGetValueAsInteger(out var opacity);
                        appearance.MouseOpacity = (MouseOpacity)opacity;
                    });
                    break;
                case "color":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        value.TryGetValueAsString(out string color);
                        color ??= "white";
                        appearance.SetColor(color);
                    });
                    break;
                case "dir":
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        //TODO figure out the weird inconsistencies with this being internally clamped
                        if (!value.TryGetValueAsInteger(out var dir))
                        {
                            dir = 2; // SOUTH
                        }
                        appearance.Direction = (AtomDirection)dir;
                    });
                    break;
                case "transform":
                {
                    _atomManager.UpdateAppearance(dreamObject, appearance => {
                        float[] matrixArray = value.TryGetValueAsDreamObjectOfType(_objectTree.Matrix, out var matrix)
                            ? DreamMetaObjectMatrix.MatrixToTransformFloatArray(matrix)
                            : DreamMetaObjectMatrix.IdentityMatrixArray;

                        appearance.Transform = matrixArray;
                    });
                    break;
                }
                case "overlays": {
                    if (oldValue.TryGetValueAsDreamList(out DreamList oldList)) {
                        oldList.Cut();
                        oldList.ValueAssigned -= OverlayValueAssigned;
                        oldList.BeforeValueRemoved -= OverlayBeforeValueRemoved;
                        _atomManager.OverlaysListToAtom.Remove(oldList);
                    }

                    DreamList overlayList;
                    if (!value.TryGetValueAsDreamList(out overlayList)) {
                        overlayList = DreamList.Create();
                    }

                    overlayList.ValueAssigned += OverlayValueAssigned;
                    overlayList.BeforeValueRemoved += OverlayBeforeValueRemoved;
                    _atomManager.OverlaysListToAtom[overlayList] = dreamObject;
                    dreamObject.SetVariableValue(varName, new DreamValue(overlayList));
                    break;
                }
                case "underlays": {
                    if (oldValue.TryGetValueAsDreamList(out DreamList oldList)) {
                        oldList.Cut();
                        oldList.ValueAssigned -= UnderlayValueAssigned;
                        oldList.BeforeValueRemoved -= UnderlayBeforeValueRemoved;
                        _atomManager.UnderlaysListToAtom.Remove(oldList);
                    }

                    DreamList underlayList;
                    if (!value.TryGetValueAsDreamList(out underlayList)) {
                        underlayList = DreamList.Create();
                    }

                    underlayList.ValueAssigned += UnderlayValueAssigned;
                    underlayList.BeforeValueRemoved += UnderlayBeforeValueRemoved;
                    _atomManager.UnderlaysListToAtom[underlayList] = dreamObject;
                    dreamObject.SetVariableValue(varName, new DreamValue(underlayList));
                    break;
                }
                case "filters": {
                    DreamFilterList filterList = _filterLists[dreamObject];

                    filterList.Cut();

                    if (value.TryGetValueAsDreamList(out var valueList)) {
                        // TODO: This should maybe postpone UpdateAppearance until after everything is added
                        foreach (DreamValue filterValue in valueList.GetValues()) {
                            filterList.AddValue(filterValue);
                        }
                    } else if (value != DreamValue.Null) {
                        filterList.AddValue(value);
                    }

                    break;
                }
            }
        }

        public DreamValue OnVariableGet(DreamObject dreamObject, string varName, DreamValue value) {
            switch (varName) {
                case "transform":
                    // Clone the matrix
                    DreamObject matrix = _objectTree.CreateObject(_objectTree.Matrix);
                    matrix.InitSpawn(new DreamProcArguments(new() { value }));

                    return new DreamValue(matrix);
                case "filters":
                    return new DreamValue(_filterLists[dreamObject]);
                default:
                    return ParentType?.OnVariableGet(dreamObject, varName, value) ?? value;
            }
        }

        private IconAppearance CreateOverlayAppearance(DreamObject atom, DreamValue value) {
            IconAppearance appearance = new IconAppearance();

            if (value.TryGetValueAsString(out string valueString)) {
                appearance.Icon = _atomManager.GetAppearance(atom)?.Icon;
                appearance.IconState = valueString;
            } else if (value.TryGetValueAsDreamObjectOfType(_objectTree.MutableAppearance, out var mutableAppearance)) {
                DreamValue icon = mutableAppearance.GetVariable("icon");
                if (icon.TryGetValueAsDreamResource(out var iconResource)) {
                    appearance.Icon = iconResource.Id;
                } else if (icon == DreamValue.Null) {
                    appearance.Icon = _atomManager.GetAppearance(atom)?.Icon;
                }

                DreamValue colorValue = mutableAppearance.GetVariable("color");
                if (colorValue.TryGetValueAsString(out string color)) {
                    appearance.SetColor(color);
                } else {
                    appearance.SetColor("white");
                }

                appearance.IconState = mutableAppearance.GetVariable("icon_state").TryGetValueAsString(out var iconState) ? iconState : null;
                mutableAppearance.GetVariable("layer").TryGetValueAsFloat(out appearance.Layer);
                mutableAppearance.GetVariable("pixel_x").TryGetValueAsInteger(out appearance.PixelOffset.X);
                mutableAppearance.GetVariable("pixel_y").TryGetValueAsInteger(out appearance.PixelOffset.Y);
            } else if (value.TryGetValueAsDreamObjectOfType(_objectTree.Image, out var image)) {
                DreamValue icon = image.GetVariable("icon");
                DreamValue iconState = image.GetVariable("icon_state");

                appearance.Icon = icon.TryGetValueAsDreamResource(out var iconResource)
                    ? iconResource.Id
                    : null;

                if (iconState.TryGetValueAsString(out var iconStateString)) appearance.IconState = iconStateString;
                var color = image.GetVariable("color").TryGetValueAsString(out var colorString)
                    ? colorString
                    : "#FFFFFF"; // Defaults to white
                appearance.SetColor(color);
                appearance.Direction = (AtomDirection) image.GetVariable("dir").GetValueAsInteger();
                image.GetVariable("layer").TryGetValueAsFloat(out appearance.Layer);
                image.GetVariable("pixel_x").TryGetValueAsInteger(out appearance.PixelOffset.X);
                image.GetVariable("pixel_y").TryGetValueAsInteger(out appearance.PixelOffset.Y);
            } else if (value.TryGetValueAsDreamObjectOfType(_objectTree.Icon, out var icon)) {
                var iconObj = DreamMetaObjectIcon.ObjectToDreamIcon[icon];
                var resource = iconObj.GenerateDMI();

                atom.GetVariable("icon_state").TryGetValueAsString(out var iconState);

                appearance.Icon = resource.Id;
                appearance.IconState = resource.DMI.GetStateOrDefault(iconState)?.Name;
            } else if (value.TryGetValueAsDreamObjectOfType(_objectTree.Atom, out var overlayAtom)) {
                appearance = _atomManager.CreateAppearanceFromAtom(overlayAtom);
            } else if (value.TryGetValueAsType(out var type)) {
                appearance = _atomManager.CreateAppearanceFromDefinition(type.ObjectDefinition);
            } else {
                throw new Exception($"Invalid overlay {value}");
            }

            return appearance;
        }

        private void OverlayValueAssigned(DreamList overlayList, DreamValue key, DreamValue value) {
            if (value == DreamValue.Null) return;

            DreamObject atom = _atomManager.OverlaysListToAtom[overlayList];

            _atomManager.UpdateAppearance(atom, appearance => {
                IconAppearance overlay = CreateOverlayAppearance(atom, value);
                uint id = EntitySystem.Get<ServerAppearanceSystem>().AddAppearance(overlay);

                appearance.Overlays.Add(id);
            });
        }

        private void OverlayBeforeValueRemoved(DreamList overlayList, DreamValue key, DreamValue value) {
            if (value == DreamValue.Null) return;

            DreamObject atom = _atomManager.OverlaysListToAtom[overlayList];
            IconAppearance overlayAppearance = CreateOverlayAppearance(atom, value);
            uint? overlayAppearanceId = EntitySystem.Get<ServerAppearanceSystem>().GetAppearanceId(overlayAppearance);
            if (overlayAppearanceId == null) return;

            _atomManager.UpdateAppearance(atom, appearance => {
                appearance.Overlays.Remove(overlayAppearanceId.Value);
            });
        }

        private void UnderlayValueAssigned(DreamList overlayList, DreamValue key, DreamValue value) {
            if (value == DreamValue.Null) return;

            DreamObject atom = _atomManager.UnderlaysListToAtom[overlayList];

            _atomManager.UpdateAppearance(atom, appearance => {
                IconAppearance underlay = CreateOverlayAppearance(atom, value);
                uint id = EntitySystem.Get<ServerAppearanceSystem>().AddAppearance(underlay);

                appearance.Underlays.Add(id);
            });
        }

        private void UnderlayBeforeValueRemoved(DreamList overlayList, DreamValue key, DreamValue value) {
            if (value == DreamValue.Null) return;

            DreamObject atom = _atomManager.UnderlaysListToAtom[overlayList];
            IconAppearance underlayAppearance = CreateOverlayAppearance(atom, value);
            uint? underlayAppearanceId = EntitySystem.Get<ServerAppearanceSystem>().GetAppearanceId(underlayAppearance);
            if (underlayAppearanceId == null) return;

            _atomManager.UpdateAppearance(atom, appearance => {
                appearance.Underlays.Remove(underlayAppearanceId.Value);
            });
        }
    }
}
