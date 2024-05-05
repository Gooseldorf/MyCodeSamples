using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class ItemPositionController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image raycastTargetImage;
    [field: SerializeField] public ScrollRectRaycastBypass ScrollRectRaycastBypass;
    public abstract IPosition TreeItem { get; }
    public ContainerType ContainerType;
    public Tweener Tweener; 

    public static event Action<PointerEventData> OnPressEvent;
    public static event Action<PointerEventData> OnDragEvent;
    public static event Action<PointerEventData> OnReleaseEvent;

    private void Start()
    {
        Messenger<bool>.AddListener(GameEvents.ActivateEditMode, ActivateDrag);
        Messenger.AddListener(GameEvents.OnDragEnd, OnDragEnd);
        Messenger<ItemPositionController>.AddListener(GameEvents.OnDragStart, OnDragStart);
    }

    private void OnDestroy()
    {
        Messenger<bool>.RemoveListener(GameEvents.ActivateEditMode, ActivateDrag);
        Messenger.RemoveListener(GameEvents.OnDragEnd, OnDragEnd);
        Messenger<ItemPositionController>.RemoveListener(GameEvents.OnDragStart, OnDragStart);
    }

    public void OnPointerDown(PointerEventData eventData) => OnPressEvent?.Invoke(eventData);

    public void OnPointerUp(PointerEventData eventData) => OnReleaseEvent?.Invoke(eventData);

    public void OnDrag(PointerEventData eventData) => OnDragEvent?.Invoke(eventData);

    public virtual void Init(IPosition item)
    {
        if(GetComponentInParent<BranchContainer>().IsMirrored)
            TurnVisual(true);
    }

    public void ToggleCanvasGroupRaycast(bool isActive) => canvasGroup.blocksRaycasts = isActive;

    public virtual void SetContainerSiblingIndex(){}

    public virtual void TurnVisual(bool isMirrored){}
    
    private void ActivateDrag(bool activate)
    {
        canvasGroup.enabled =
            canvasGroup.interactable = activate;
    }

    private void OnDragStart(ItemPositionController treeItem)
    {
        if (treeItem != this)
            raycastTargetImage.raycastTarget = false;
    }

    private void OnDragEnd() => raycastTargetImage.raycastTarget = true;
}
