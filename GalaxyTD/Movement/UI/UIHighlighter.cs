using DG.Tweening;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    public class UIHighlighter : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<UIHighlighter> { }

        private VisualElement topContainer;
        private VisualElement leftContainer;
        private VisualElement rightContainer;
        private VisualElement bottomContainer;
        private VisualElement[] sideContainers;
        private VisualElement mainHighlight;
        private VisualElement glow;
        private VisualElement additionalGlow;
        private Rect additionalHighlightRect;
        
        public void Init()
        {
            topContainer = this.Q<VisualElement>("TopContainer");
            leftContainer = this.Q<VisualElement>("LeftContainer");
            rightContainer = this.Q<VisualElement>("RightContainer");
            bottomContainer = this.Q<VisualElement>("BottomContainer");
            sideContainers = new [] { topContainer, leftContainer, rightContainer, bottomContainer };
            mainHighlight = this.Q<VisualElement>("MainHighlight");

            SetUpGlowAnimation();
        }

        public void ShowMainHighlighter(float2 position, float2 size)
        {
            leftContainer.style.width = position.x;
            bottomContainer.style.height = position.y; 
            mainHighlight.style.width = size.x;
            mainHighlight.style.height = size.y;
            ShowGlow(glow, position, size);
            mainHighlight.schedule.Execute(ShowAdditionalHighlight).ExecuteLater(50);
        }

        public void Reset()
        {
            additionalHighlightRect = new();
            foreach (VisualElement container in sideContainers)
            {
                container.Q<VisualElement>("Highlight").style.width = 0;
                container.Q<VisualElement>("Highlight").style.height = 0;
                container.Q<VisualElement>("Left").style.width = 0;
                container.Q<VisualElement>("Bottom").style.height = 0;
            }
            ShowMainHighlighter(float2.zero, float2.zero);
            glow.style.visibility = Visibility.Hidden;
            additionalGlow.style.visibility = Visibility.Hidden;
        }

        private void SetUpGlowAnimation()
        {
            glow = this.Q<VisualElement>("Glow");
            UIHelper.Instance.FadeTween(glow, 0.2f, 1f, 1).SetUpdate(true).SetTarget(glow).SetLoops(-1, LoopType.Yoyo).Pause();
            additionalGlow = this.Q<VisualElement>("AdditionalGlow");
            UIHelper.Instance.FadeTween(additionalGlow, 0.2f, 1f, 1).SetUpdate(true).SetTarget(additionalGlow).SetLoops(-1, LoopType.Yoyo).Pause();
        }

        public void SetUpAdditionalHighlighter(float2 position, float2 size) => additionalHighlightRect = new Rect(position, size);

        private void ShowAdditionalHighlight()
        {
            if (additionalHighlightRect.position == Vector2.zero || additionalHighlightRect.size == Vector2.zero)
                return;
            
            Dictionary<VisualElement, Overlap> overlaps = GetOverlaps(additionalHighlightRect.position, additionalHighlightRect.size);

            foreach (KeyValuePair<VisualElement, Overlap> pair in overlaps)
            {
                pair.Key.Q<VisualElement>("Left").style.width = pair.Value.LocalPosition.x;
                pair.Key.Q<VisualElement>("Bottom").style.height = pair.Value.LocalPosition.y;
                VisualElement highlight = pair.Key.Q<VisualElement>("Highlight");
                highlight.style.width = pair.Value.Size.x;
                highlight.style.height = pair.Value.Size.y;
            }
            
            ShowGlow(additionalGlow, additionalHighlightRect.position, additionalHighlightRect.size);
        }

        private void ShowGlow(VisualElement glowElement, float2 position, float2 size)
        {
            glowElement.style.visibility = Visibility.Visible;
            DOTween.Play(glowElement);
            glowElement.style.width = size.x + 20;
            glowElement.style.height = size.y + 20;

            glowElement.style.left = position.x - 10;
            glowElement.style.bottom = position.y - 10;
        }

        private Dictionary<VisualElement, Overlap> GetOverlaps(float2 position, float2 size)
        {
            Dictionary<VisualElement, Overlap> result = new();
            foreach (VisualElement container in sideContainers)
            {
                Rect containerRect = new Rect(container.worldBound.x, 1080 - container.worldBound.height- container.worldBound.y, container.worldBound.width, container.worldBound.height);
                Rect testRect = new Rect(position.x, position.y, size.x, size.y);
        
                if (testRect.Overlaps(containerRect))
                {
                    Rect overlapRect = Rect.MinMaxRect(Math.Max(containerRect.xMin, testRect.xMin), Math.Max(containerRect.yMin, testRect.yMin), 
                        Math.Min(containerRect.xMax, testRect.xMax), Math.Min(containerRect.yMax, testRect.yMax));
            
                    Overlap overlap = new Overlap
                    {
                        LocalPosition = new float2(overlapRect.x - containerRect.x, overlapRect.y - containerRect.y),
                        Size = new float2(overlapRect.width, overlapRect.height)
                    };
                    result.Add(container, overlap);
                }
            }

            return result;
        }
        
        private struct Overlap
        {
            public float2 LocalPosition;
            public float2 Size;
        }
    }
}
