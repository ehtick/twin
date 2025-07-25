using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Netherlands3D.Functionalities.OGC3DTiles;
using Netherlands3D.Functionalities.Wms;
using Netherlands3D.Twin.ExtensionMethods;
using Netherlands3D.Twin.Layers.LayerTypes;
using Netherlands3D.Twin.Layers.LayerTypes.CartesianTiles;
using Netherlands3D.Twin.Layers.LayerTypes.GeoJsonLayers;
using Netherlands3D.Twin.Layers.LayerTypes.HierarchicalObject;
using Netherlands3D.Twin.Layers.LayerTypes.Polygons;
using Netherlands3D.Twin.Projects;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Netherlands3D.Twin.Layers.UI.HierarchyInspector
{
    public class LayerUIManager : MonoBehaviour, IPointerDownHandler
    {
        public List<LayerUI> LayerUIsVisibleInInspector { get; private set; } = new List<LayerUI>();

        [SerializeField] private LayerUI LayerUIPrefab;
        [SerializeField] private List<Sprite> layerTypeSprites;
        [SerializeField] private RectTransform layerUIContainer;

        public RectTransform LayerUIContainer => layerUIContainer;

        private Dictionary<LayerData, LayerUI> layerUIDictionary = new();

        //Drag variables
        [SerializeField] private DragGhost dragGhostPrefab;
        public DragGhost DragGhost { get; private set; }
        [SerializeField] private RectTransform dragLine;
        public RectTransform DragLine => dragLine;
        public Vector2 DragStartOffset { get; set; }
        public bool MouseIsOverButton { get; set; }
        
        private LayerData layerToSelectAtEndOfFrame; //only select a layer once at the end of the frame in case multiple layers are made in this frame to avoid an unnecessary performance hit.

        private void ReconstructHierarchyUIs()
        {
            DestroyAllUIs();
            foreach (var layer in ProjectData.Current.RootLayer.ChildrenLayers)
            {
                ConstructHierarchyUIsRecursive(layer, ProjectData.Current.RootLayer);
            }

            RecalculateLayersVisibleInInspector();
        }

        private void ConstructHierarchyUIsRecursive(LayerData layer, LayerData parent)
        {
            InstantiateLayerItem(layer, parent);
            foreach (var child in layer.ChildrenLayers)
            {
                ConstructHierarchyUIsRecursive(child, layer);
            }
        }

        private LayerUI InstantiateLayerItem(LayerData layer, LayerData parent)
        {
            var layerUI = Instantiate(LayerUIPrefab, LayerUIContainer);
            layerUI.Layer = layer;
            layerUIDictionary.Add(layer, layerUI);
            layerUI.name = layer.Name;
            
            if (parent is not RootLayer)
                layerUI.SetParent(GetLayerUI(parent), layer.SiblingIndex);

            return layerUI;
        }

        private void DestroyAllUIs()
        {
            foreach (Transform t in LayerUIContainer)
            {
                t.gameObject.SetActive(false); //ensure it won't get re-added in RecalculateLayersInInspector
                Destroy(t.gameObject);
            }

            layerUIDictionary = new();
        }

        private void OnEnable()
        {
            ReconstructHierarchyUIs();
            ProjectData.Current.LayerAdded.AddListener(CreateNewUI);
            ProjectData.Current.LayerDeleted.AddListener(OnLayerDeleted);
            ProjectData.Current.OnDataChanged.AddListener(OnProjectDataChanged);
        }

        private void OnDisable()
        {
            ProjectData.Current.RootLayer.DeselectAllLayers();
            ProjectData.Current.LayerAdded.RemoveListener(CreateNewUI);
            ProjectData.Current.LayerDeleted.RemoveListener(OnLayerDeleted);
            ProjectData.Current.OnDataChanged.RemoveListener(OnProjectDataChanged);
        }

        private void OnProjectDataChanged(ProjectData data)
        {
            ReconstructHierarchyUIs(); //ensure a clean ui hierarchy after a project is loaded 
        }

        private void CreateNewUI(LayerData layer)
        {
            var layerUI = InstantiateLayerItem(layer, layer.ParentLayer);
            RecalculateLayersVisibleInInspector();
            
            if(layerToSelectAtEndOfFrame == null)
                StartCoroutine(SelectLayerAtEndOfFrame());
    
            layerToSelectAtEndOfFrame = layer;
        }

        private IEnumerator SelectLayerAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            layerToSelectAtEndOfFrame.SelectLayer(true);
            layerToSelectAtEndOfFrame = null;
        }

        private void OnRectTransformDimensionsChange()
        {
            foreach (var layer in LayerUIsVisibleInInspector)
            {
                layer.MarkLayerUIAsDirty();
            }
        }

        public void StartDragLayer(LayerUI layerUI)
        {
            CreateGhost(layerUI);
        }

        public void EndDragLayer()
        {
            // DraggingLayer = null;
            Destroy(DragGhost.gameObject);
            dragLine.gameObject.SetActive(false);
        }

        private void CreateGhost(LayerUI ui)
        {
            DragGhost = Instantiate(dragGhostPrefab, transform);
            DragGhost.GetComponent<RectTransform>().SetLeft(layerUIContainer.offsetMin.x);
            DragGhost.Initialize(DragStartOffset, ui);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!LayerUI.AddToSelectionModifierKeyIsPressed() && !LayerUI.SequentialSelectionModifierKeyIsPressed())
                ProjectData.Current.RootLayer.DeselectAllLayers();
        }

        public FolderLayer CreateFolderLayer()
        {
            var folder = new FolderLayer("Folder");
            return folder;
        }

        public Sprite GetLayerTypeSprite(LayerData layer)
        {
            switch (layer)
            {
                case PolygonSelectionLayer selectionLayer:
                    if (selectionLayer.ShapeType == ShapeType.Line)
                        return layerTypeSprites[7];
                    else if (selectionLayer.ShapeType == ShapeType.Grid)
                        return layerTypeSprites[12];
                    return layerTypeSprites[6];
                case ReferencedLayerData data:
                    var reference = data.Reference;
                    return reference == null ? layerTypeSprites[0] : GetProxyLayerSprite(reference);
                case FolderLayer _:
                    return layerTypeSprites[2];
                default:
                    Debug.LogError("layer type of " + layer.Name + " is not specified");
                    return layerTypeSprites[0];
            }
        }

        private Sprite GetProxyLayerSprite(LayerGameObject layer)
        {
            switch (layer)
            {
                case WMSLayerGameObject _:
                case GeoJsonLayerGameObject _:
                    return layerTypeSprites[8];
                case CartesianTileLayerGameObject _:
                case Tile3DLayerGameObject _:
                    return layerTypeSprites[1];
                case WorldAnnotationLayerGameObject _:
                    return layerTypeSprites[10];
                case CameraPositionLayerGameObject _:
                    return layerTypeSprites[11];
                case HierarchicalObjectLayerGameObject _:
                    return layerTypeSprites[3];
                case ObjectScatterLayerGameObject _:
                    return layerTypeSprites[4];
                case CartesianTileSubObjectColorLayerGameObject _:
                    return layerTypeSprites[5];
                case GeoJSONPolygonLayer _:
                    return layerTypeSprites[6];
                case GeoJSONLineLayer _:
                    return layerTypeSprites[7];                
                case GeoJSONPointLayer _:
                    return layerTypeSprites[9];
                default:
                    Debug.LogError("layer type of " + layer.Name + " is not specified");
                    return layerTypeSprites[0];
            }
        }

        private void Update()
        {
            if (Keyboard.current.deleteKey.wasPressedThisFrame && !EventSystem.current.currentSelectedGameObject)
            {
                DeleteSelectedLayers();
            }
        }

        public void GroupSelectedLayers()
        {
            if (ProjectData.Current.RootLayer.SelectedLayers.Count == 0) 
                return;
            
            var layersToGroup = new List<LayerData>(ProjectData.Current.RootLayer.SelectedLayers); //make a copy because creating a new folder layer will cause this new layer to be selected and therefore the other layers to be deselected.

            var newGroup = CreateFolderLayer();
            var referenceLayer = ProjectData.Current.RootLayer.SelectedLayers.Last();
            newGroup.SetParent(referenceLayer.ParentLayer, referenceLayer.SiblingIndex);
            SortSelectedLayers(layersToGroup);
            foreach (var selectedLayer in layersToGroup)
            {
                selectedLayer.SetParent(newGroup);
            }
        }

        public void SortSelectedLayersByVisibility()
        {
            ProjectData.Current.RootLayer.SelectedLayers.Sort((layer1, layer2) => LayerUIsVisibleInInspector.IndexOf(GetLayerUI(layer1)).CompareTo(LayerUIsVisibleInInspector.IndexOf(GetLayerUI(layer2))));
        }

        private void SortSelectedLayers(List<LayerData> selectedLayers)
        {
            selectedLayers.Sort((layer1, layer2) => LayerUIsVisibleInInspector.IndexOf(GetLayerUI(layer1)).CompareTo(LayerUIsVisibleInInspector.IndexOf(GetLayerUI(layer2))));
        }

        public bool IsDragOnButton()
        {
            return DragGhost && MouseIsOverButton;
        }

        public void DeleteSelectedLayers()
        {
            foreach (var layer in ProjectData.Current.RootLayer.SelectedLayers.ToList()) //to list makes a copy and avoids a collectionmodified error
            {
                layer.DestroyLayer();
            }
        }

        private void OnLayerDeleted(LayerData layer)
        {
            layerUIDictionary.Remove(layer);
        }

        public void RecalculateLayersVisibleInInspector()
        {
            LayerUIsVisibleInInspector.Clear();
            LayerUIsVisibleInInspector = layerUIContainer.GetComponentsInChildren<LayerUI>(false).ToList();
        }

        public LayerUI GetLayerUI(LayerData layer)
        {
            if (layer is RootLayer)
                return null;
            
            return layerUIDictionary[layer];
        }

        public void HighlightLayerUI(LayerData layer, bool isOn)
        {
            if (layer.IsSelected)
                return;

            var layerState = isOn ? InteractionState.Hover : InteractionState.Default;
            var ui = GetLayerUI(layer);
            ui.SetHighlight(layerState);
            ui.MarkLayerUIAsDirty();
        }
    }
}