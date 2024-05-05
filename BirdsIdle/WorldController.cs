using CardTD.NoAssembly;
using UnityEngine;
using Sirenix.OdinInspector;
using UI;
using Managers;


public class WorldController : MonoBehaviour
    {
        [SerializeField, Required] UIManager uiManager;
        [SerializeField] private TreeController treeController;
        [SerializeField] private int offlineIncomeMessageDelay;

        private bool isInited = false;

        private void Awake()
        {
            Init();
        }

        private async void Init()
        {
            VibrationManager.ToggleVibration(PlayerPrefs.GetInt(PrefKeys.Vibro, 1) == 1);
            SettingsPanel.SetLanguage(PlayerPrefs.GetString(PrefKeys.Language, "en"));
            await IAPManager.InitServices();
            PlayerData.Load();
            EffectVisualManager.Instance.Init();
            TimeManager.Instance.CalculateCurrentTime();
            if (treeController != null)
                treeController.Init();
            uiManager.Init(PlayerData.Instance.CurrentTree);
            treeController.CheckEmptyContainersExists();
            isInited = true;
        }

        private void Update()
        {
            if (!isInited)
                return;

            TimeManager.Instance.CalculateCurrentTime();
            PlayerData.Instance.CalculateMultiplier();
            if (PlayerData.Instance.CurrentTree.Birds.Count > 0)
            {
                foreach (var bird in PlayerData.Instance.CurrentTree.Birds)
                    bird.GenerateNotesWithOffset();
            }

            if ((PlayerData.Instance.TapBooster as ICooldownable).IsActive)
                PlayerData.Instance.TapBooster.ApplyEffect();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!isInited)
                return;

            if (pauseStatus)
            {
                PlayerData.Instance.CurrentTree.LastActivityTime = TimeManager.Instance.CurrentTime;
                PlayerData.Instance.Save();
            }
            else
            {
                TimeManager.Instance.CalculateCurrentTime();
                double offlineIncome = PlayerData.Instance.CurrentTree.CalculateOfflineIncome();
                if (TimeManager.Instance.CurrentTime - PlayerData.Instance.CurrentTree.LastActivityTime >= offlineIncomeMessageDelay)
                {
                    if (offlineIncome > 0)
                        Messenger<UIManager.PanelType, object>.Broadcast(GameEvents.ShowPanel, UIManager.PanelType.OfflineEarnings, offlineIncome, MessengerMode.DONT_REQUIRE_LISTENER);
                }
                else
                    PlayerData.Instance.ChangeNotes(offlineIncome);
            }
        }

#if UNITY_EDITOR
        private void OnApplicationQuit() => OnApplicationPause(true);
#endif
    }

