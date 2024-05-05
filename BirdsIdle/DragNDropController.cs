using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DragNDropController : MonoBehaviour
{
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private float scrollSpeed;
    [SerializeField] private GameObject scrollOnDragAreaGroup;
    [SerializeField] private Image raycastBlocker;
    
    private ItemPositionController selectedTreeItem;
    private ItemPositionController raycastHitTreeItem;
    private BranchContainer selectedContainer;
    private BranchContainer raycastHitContainer;
    private ScrollOnDragArea scrollOnDragArea;
    private bool isMoving;
    private float moveDirection;
    private Vector3 offset;

    private bool isCanDrag;

    private void Start()
    {
        Messenger<bool>.AddListener(GameEvents.ActivateEditMode, ActivateDragNDrop);
        ItemPositionController.OnPressEvent += OnPointerDown;
        ItemPositionController.OnDragEvent += OnDrag;
        ItemPositionController.OnReleaseEvent += OnPointerUp;
    }

    private void Update()
    {
        if (isMoving)
        {
            scrollRect.DOVerticalNormalizedPos(Mathf.Clamp01(scrollRect.normalizedPosition.y + moveDirection * scrollSpeed), Time.deltaTime); 
            if (scrollRect.normalizedPosition.y >= 1)
            {
                isMoving = false;
                return;
            }
            selectedTreeItem.transform.DOMoveY(selectedTreeItem.transform.position.y + moveDirection * scrollSpeed, Time.deltaTime);
        }
    }

    private void OnDestroy()
    {
        Messenger<bool>.RemoveListener(GameEvents.ActivateEditMode, ActivateDragNDrop);
        ItemPositionController.OnPressEvent -= OnPointerDown;
        ItemPositionController.OnDragEvent -= OnDrag;
        ItemPositionController.OnReleaseEvent -= OnPointerUp;
    }

    private void ActivateDragNDrop(bool activate)
    {
        isCanDrag = activate;
        scrollOnDragAreaGroup.SetActive(isCanDrag);
    }

    private void OnPointerDown(PointerEventData eventData)
    {
        if (!isCanDrag) return;

        RaycastResult raycastResult = eventData.pointerCurrentRaycast;
        bool isItemHit = raycastResult.gameObject.TryGetComponent(out selectedTreeItem);
        if (isItemHit)
        {
            selectedContainer = selectedTreeItem.gameObject.GetComponentInParent<BranchContainer>();
            raycastHitContainer = selectedContainer; 
            offset = selectedTreeItem.transform.position - Input.mousePosition;
            selectedTreeItem.ToggleCanvasGroupRaycast(false);
            scrollRect.velocity = Vector2.zero;
            scrollRect.enabled = false;
            Messenger<ItemPositionController>.Broadcast(GameEvents.OnDragStart, selectedTreeItem);
        }
    }

    private void OnDrag(PointerEventData eventData)
    {
        if (!isCanDrag) return;
        if (!isCanDrag || selectedTreeItem == null) return;
        selectedTreeItem.transform.position = Input.mousePosition + offset;
        RaycastResult raycastResult = eventData.pointerCurrentRaycast;
        CheckScrollMove(eventData);
        bool isContainerHit = raycastResult.gameObject.TryGetComponent(out BranchContainer containerHit);
        if (isContainerHit)
        {
            if (containerHit != raycastHitContainer || containerHit == selectedContainer) ReturnAndCleanRaycastHit();

            if (containerHit == selectedContainer || containerHit == raycastHitContainer) return;

            raycastHitContainer = containerHit;

            if (!containerHit.IsEmpty)
            {
                raycastHitTreeItem = containerHit.GetComponentInChildren<ItemPositionController>();
                MoveToContainer(selectedContainer, raycastHitTreeItem);
            }
        }
        else
        { 
            ReturnAndCleanRaycastHit();
        }
    }

    private void OnPointerUp(PointerEventData eventData)
    {
        if (!isCanDrag) return;

        raycastBlocker.enabled = true;
        isMoving = false;
        scrollRect.velocity = Vector2.zero;

        RaycastResult raycastResult = eventData.pointerCurrentRaycast;
        bool isContainerHit = raycastResult.gameObject.TryGetComponent(out BranchContainer hitContainer);
        if (isContainerHit && raycastHitContainer != selectedContainer && selectedContainer.ContainerType == raycastHitContainer.ContainerType)
        {
            selectedContainer.IsEmpty = raycastHitContainer.IsEmpty;
            MoveToContainer(raycastHitContainer, selectedTreeItem, true);
        }
        else
        {
            ReturnAndCleanRaycastHit();
            if (selectedTreeItem != null)
                MoveToContainer(selectedContainer, selectedTreeItem, true);
        }
        
        raycastHitTreeItem = null;
        raycastHitContainer = null;
        selectedTreeItem = null;
        selectedContainer = null;
        scrollRect.enabled = true;
        Messenger.Broadcast(GameEvents.OnDragEnd);
    }

    private void MoveToContainer(BranchContainer container, ItemPositionController item, bool disableRaycastBlocker = false)
    {
        if(container.ContainerType != item.ContainerType) return;
        item.ToggleCanvasGroupRaycast(false);
        item.TreeItem.BranchPosition = container.BranchPosition;
        item.TreeItem.PositionIndex = container.PositionIndex;
        container.IsEmpty = false;
        item.transform.SetParent(container.transform);
        item.SetContainerSiblingIndex();
        CleanTweener(ref item.Tweener); 
        item.Tweener = item.transform.DOLocalMove(Vector3.zero, 0.4f);
        item.Tweener.OnComplete(() =>
        {
            item.ToggleCanvasGroupRaycast(true);
            item.TurnVisual(container.IsMirrored);
            if(disableRaycastBlocker)
                raycastBlocker.enabled = false;
            CleanTweener(ref item.Tweener);
        });
    }

    private void ReturnAndCleanRaycastHit()
    {
        if(raycastHitTreeItem != null)
            MoveToContainer(raycastHitContainer, raycastHitTreeItem);
        raycastHitTreeItem = null;
    }

    private void CleanTweener(ref Tweener T)
    {
        if (T != null)
        {
            T.Kill();
            T = null;
        }
    }

    private void CheckScrollMove(PointerEventData eventData)
    {
        RaycastResult raycastResult = eventData.pointerCurrentRaycast;
        bool isScrollOnDragAreaHit = raycastResult.gameObject.TryGetComponent(out scrollOnDragArea);
        if (isScrollOnDragAreaHit)
        {
            ReturnAndCleanRaycastHit();
            isMoving = true;
            moveDirection = scrollOnDragArea.Direction;
        }
        else
        {
            isMoving = false;
        }
    }
}
