using System;
using UnityEngine;

public class DecorController : ItemPositionController
{
    [SerializeField] private DecorVisual decorVisual;
    [SerializeField] private bool isBehindBranch;
    private Decor decor;
    public override IPosition TreeItem => decor;
    
    public override void Init(IPosition item)
    {
        decor = item as Decor;
        ContainerType = ContainerType.Common;
        decorVisual.Init();
        base.Init(item);
        SetContainerSiblingIndex();
    }

    public override void SetContainerSiblingIndex()
    {
        if(isBehindBranch)
            transform.parent.SetAsFirstSibling();
        else
            transform.parent.SetAsLastSibling();
    }

    public override void TurnVisual(bool isMirrored)
    {
        if(isMirrored)
            decorVisual.VisualsRect.localScale = new Vector3(-0.8f, 0.8f, 1);
        else
            decorVisual.VisualsRect.localScale = new Vector3(0.8f, 0.8f, 1);
    }
}
