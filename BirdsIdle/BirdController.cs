using Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Controllers
{
    public class BirdController : ItemPositionController
    {
        [SerializeField] private BirdVisual birdVisual;
        [SerializeField] private Button birdButton;
        [System.NonSerialized] public Bird Bird;

        public override IPosition TreeItem => Bird;

        public override void Init(IPosition item)
        {
            Bird = item as Bird;
            ContainerType = Bird.ContainerType;
            birdVisual.Init(Bird);
            base.Init(item);
            SetContainerSiblingIndex();
            birdButton.onClick.AddListener(OnTap);
            Bird.NotesGain += HandleNotesGain;
            Bird.OnLevelUp += birdVisual.UpdateStage;
        }

        
        public void Update()
        {
            if (Bird == null || Bird.Level != 0) return;
            if (Bird.HatchTime >= TimeManager.Instance.CurrentTime)
            {
                float incubationTimeLeft = Bird.HatchTime - TimeManager.Instance.CurrentTime;
                float incubationPercentage = 1 - incubationTimeLeft / Bird.Data.IncubationTime;
                birdVisual.DisplayIncubationTime(incubationTimeLeft, incubationPercentage);
                return;
            }
            Bird.LevelUp();
        }

        public override void SetContainerSiblingIndex()
        {
            if (Bird.Level >= 20)
                transform.parent.SetAsFirstSibling();
            else
                transform.parent.SetAsLastSibling();
        }

        public override void TurnVisual(bool isMirrored)
        {
            if (isMirrored)
                birdVisual.StageVisuals.localScale = new Vector3(-1,1,1);
            else
                birdVisual.StageVisuals.localScale = Vector3.one;
            birdVisual.UpdateNoteVisualRoot();
        }

        private void HandleNotesGain(double notes, bool isTap)
        {
            if (isTap)
            {
                birdVisual.DisplayNotesText(notes);
                birdVisual.ShowTapEffect();
            }
            else
            {
                birdVisual.DisplayNotesText(notes);
                birdVisual.ShowNotesVisual();
                if(Bird.Level >= 20)
                    StartCoroutine(birdVisual.ShowBirdAnimation());
            }
        }

        private void OnTap()
        {
            if(Bird.Level <= 0) return;
            Bird.GetNotes(true);
            VibrationManager.Vibrate(50, 150);
            SoundManager.Instance.PlayBirdSound();
        }

        private void OnDestroy()
        {
            if (Bird != null)
            {
                Bird.NotesGain -= HandleNotesGain;
                Bird.OnLevelUp -= birdVisual.UpdateStage;
            }
            birdButton.onClick.RemoveAllListeners();
        }
    }
}
