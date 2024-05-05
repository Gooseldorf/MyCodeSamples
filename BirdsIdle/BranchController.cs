using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BranchController : MonoBehaviour
    {
        [SerializeField] private List<BranchContainer> birdContainers;
        [SerializeField] private BranchVisual branchVisual;
        [SerializeField] private Button buyBranchButton;
        
        private Branch branch;
        private double decorUnlockCostBonus;
        
        public bool IsLocked = false;
        
        public List<BranchContainer> BirdContainers => birdContainers;
        public event Action<Branch> OnBranchUnlock; 
        
        private void Start()
        {
            buyBranchButton.onClick.AddListener(TryBuyBranch);
        }

        private void OnDestroy()
        {
            buyBranchButton.onClick.RemoveListener(TryBuyBranch);            
        }

        public void Init(int branchPosition, Branch branch)
        {
            this.branch = branch;
            for (int i = 0; i < BirdContainers.Count; i++)
            {
                BirdContainers[i].InitWithPosition(branchPosition, i);
            }
            UpdateCondition();
        }

        public void ToggleLock(bool isLocked, bool isLockShown)
        {
            IsLocked = isLocked;
            branchVisual.ToggleLock(isLocked);
            branchVisual.ShowLock(isLockShown);
            buyBranchButton.interactable = isLockShown;
        }

        public void UpdateCondition()
        {
            string text;
            decorUnlockCostBonus = branch.UnlockCost * PlayerData.Instance.CurrentTree.GetDecorBonus("decor_new_branch_cost");
            if (branch.UnlockCondition.CheckCondition())
            {
                text = Utilities.GetNotesString(branch.UnlockCost + decorUnlockCostBonus);
                branchVisual.ShowCostCondition(text);
            }
            else
            {
                text = branch.UnlockCondition.GetCondition();
                branchVisual.ShowLevelCondition(text);
            }
        }

        private void TryBuyBranch()
        {
            if(!branch.UnlockCondition.CheckCondition() || PlayerData.Instance.Notes < branch.UnlockCost + decorUnlockCostBonus ) return;
            PlayerData.Instance.ChangeNotes(branch.UnlockCost + decorUnlockCostBonus);
            IsLocked = false;
            ToggleLock(false, false);
			SoundManager.Instance.PlaySound("Unlock");
            OnBranchUnlock?.Invoke(branch);
            Messenger<bool, bool>.Broadcast(GameEvents.EmptyContainersCheck, true, branch.ContainerTypes.Contains(ContainerType.Special), MessengerMode.DONT_REQUIRE_LISTENER);
        }

        public void ToggleContainers(bool isActive, ContainerType type)
        {
            if(IsLocked) return;
            for (int i = 0; i < birdContainers.Count; i++)
            {
                if (birdContainers[i].IsEmpty && birdContainers[i].ContainerType == type)
                    birdContainers[i].ToggleContainer(isActive);
            }
        }

        public void DisableAllContainers()
        {
            if(IsLocked) return;
            for (int i = 0; i < birdContainers.Count; i++)
            {
                birdContainers[i].ToggleContainer(false);
            }
        }
    }

