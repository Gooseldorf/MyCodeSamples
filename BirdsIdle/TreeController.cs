using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

public class TreeController : MonoBehaviour
    {
        [SerializeField] private RectTransform branchRoot;
        [SerializeField] private BackgroundButton backgroundButton;
        [SerializeField] private ScrollRect scrollRect;

        private List<BranchController> branchControllers = new();
        private Tree tree;
        private IPosition selectedIPosition;

        private void Start()
        {
            Messenger<Bird>.AddListener(GameEvents.PlantBird, ShowEmptyContainers);
            Messenger<Decor>.AddListener(GameEvents.PlantDecor, ShowEmptyContainers);
            Messenger<BranchContainer>.AddListener(GameEvents.OnContainerClick, OnEmptyContainerClick);
            Messenger.AddListener(GameEvents.OnPlayerLevelUp, UpdateLastBranchCondition);
            backgroundButton.OnBackgroundButtonClicked += DisableAllContainers;
        }

        private void OnDestroy()
        {
            Messenger<Bird>.RemoveListener(GameEvents.PlantBird, ShowEmptyContainers);
            Messenger<Decor>.RemoveListener(GameEvents.PlantDecor, ShowEmptyContainers);
            Messenger<BranchContainer>.RemoveListener(GameEvents.OnContainerClick, OnEmptyContainerClick);
            backgroundButton.OnBackgroundButtonClicked += DisableAllContainers;
            Messenger.RemoveListener(GameEvents.OnPlayerLevelUp, UpdateLastBranchCondition);
            foreach (var branchController in branchControllers)
                branchController.OnBranchUnlock -= InitNewBranch;
        }

        public void Init()
        {
            tree = PlayerData.Instance.CurrentTree;
            InitTreeVisual();
            LoadIPositions();
        }

        public void CheckEmptyContainersExists()
        {
            bool commonExists = false;
            bool specialExists = false;
            foreach (var branchController in branchControllers)
            {
                if (branchController.IsLocked) continue;
                if (branchController.BirdContainers.Exists(x => x.IsEmpty && x.ContainerType == ContainerType.Common))
                    commonExists = true;
                
                if (branchController.BirdContainers.Exists(x => x.IsEmpty && x.ContainerType == ContainerType.Special))
                    specialExists = true;
                
            }
            Messenger<bool, bool>.Broadcast(GameEvents.EmptyContainersCheck, commonExists, specialExists, MessengerMode.DONT_REQUIRE_LISTENER);
        }

        private void InitTreeVisual()
        {
            BranchController currentController;
            for (int i = 0; i < tree.Branches.Count; i++)
            {
                currentController = InstantiateBranch(tree.Branches[i], i);
                currentController.ToggleLock(false, false);
            }
            currentController = InstantiateBranch(tree.GetBranch(branchControllers.Count), branchControllers.Count);
            currentController.ToggleLock(true, true);
            currentController = InstantiateBranch(tree.GetBranch(branchControllers.Count), branchControllers.Count);
            currentController.ToggleLock(true, false);
        }

        private BranchController InstantiateBranch(Branch branch, int branchPosition)
        {
            BranchVisual visual = EffectVisualManager.Instance.GetBranchVisual(branchPosition, branch.ContainerTypes);
            visual = Instantiate(visual, branchRoot.position, Quaternion.identity, branchRoot);
            BranchController controller = visual.gameObject.GetComponent<BranchController>();
            controller.Init(branchControllers.Count, branch);
            branchControllers.Add(controller);
            controller.OnBranchUnlock += InitNewBranch;
            return controller;
        }

        private void InitNewBranch(Branch branch)
        {
            PlayerData.Instance.CurrentTree.AddNewBranch(branch);
            BranchController controller = InstantiateBranch(tree.GetBranch(tree.Branches.Count + 1), branchControllers.Count);
            controller.ToggleLock(true, false);
            branchControllers[^2].ToggleLock(true, true);
        }

        private void UpdateLastBranchCondition() => branchControllers[^1].UpdateCondition();

        private void LoadIPositions()
        {
            foreach (var bird in tree.Birds)
            {
                if (bird.BranchPosition != -1 && bird.PositionIndex != -1)
                    InstantiateTreeItem(bird);
            }
            foreach (var decor in tree.Decors)
            {
                if (decor.BranchPosition != -1 && decor.PositionIndex != -1)
                    InstantiateTreeItem(decor);
            }
        }

        private void ShowEmptyContainers(IPosition treeItem)
        {
            backgroundButton.Toggle(true);
            selectedIPosition = treeItem;
            switch(treeItem)
            {
                case Bird bird:
                    foreach (BranchController branch in branchControllers)
                        branch.ToggleContainers(true, bird.ContainerType);
                    FindAndFocusOnEmptyContainer(bird.ContainerType);
                    break;
                case Decor decor:
                    foreach (BranchController branch in branchControllers)
                        branch.ToggleContainers(true, ContainerType.Common);
                    FindAndFocusOnEmptyContainer(ContainerType.Common);
                    break;
                default:
                    throw new Exception("Unknown IPosition");
            }
        }

        private void OnEmptyContainerClick(BranchContainer container)
        {
            switch (selectedIPosition)
            {
                case Bird bird:
                    bird.Place(container.BranchPosition, container.PositionIndex);
                    InstantiateTreeItem(bird);
                    break;
                case Decor decor:
                    decor.Place(container.BranchPosition, container.PositionIndex);
                    InstantiateTreeItem(decor);
                    break;
                default:
                    throw new Exception("Unknown IPosition");
            }
            container.ToggleContainer(false);
            SoundManager.Instance.PlaySound("Place");
            DisableAllContainers();
            CheckEmptyContainersExists();
        }

        private void InstantiateTreeItem(IPosition item)
        {
            BranchContainer container = branchControllers[item.BranchPosition].BirdContainers[item.PositionIndex];
            ItemPositionController prefab = null;
            switch (item)
            {
                case Bird bird:
                    prefab = EffectVisualManager.Instance.GetTreeItem(bird.DataID);
                    break;
                case Decor decor:
                    prefab = EffectVisualManager.Instance.GetTreeItem(decor.DataID);
                    break;
                default:
                    throw new Exception($"Couldn't create tree item on position {container.BranchPosition}:{container.PositionIndex}");
            }
            ItemPositionController positionController = Instantiate(prefab, container.transform);
            positionController.Init(item);
            positionController.ScrollRectRaycastBypass.scroll = scrollRect;
            container.IsEmpty = false;
        }

        private void FindAndFocusOnEmptyContainer(ContainerType containerType)
        {
            for (int i = 0; i < branchControllers.Count; i++)
            {
                if (branchControllers[i].BirdContainers
                    .Exists(x => x.IsEmpty == true && x.ContainerType == containerType))
                {
                    FocusOnItem(branchControllers[i]);
                    return;
                }
            }
        }

        private void FocusOnItem(BranchController branch)
        {
            StartCoroutine(scrollRect.FocusOnItemCoroutine(branch.GetComponent<RectTransform>(), 3f));
        }

        private void DisableAllContainers()
        {
            foreach (BranchController branch in branchControllers)
                branch.DisableAllContainers();
            backgroundButton.Toggle(false);
        }
    }

