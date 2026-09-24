using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// UI Toolkit helper utilities for creating consistent components across the UnityUtils dashboard.
    /// </summary>
    internal static class UnityUtilsUIStyle
    {
        public static VisualElement CreateHeader(string title, string subtitle, string version)
        {
            var header = new VisualElement();
            header.AddToClassList("header-banner");

            var titleGroup = new VisualElement();
            titleGroup.AddToClassList("header-title-group");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("header-title");

            var subtitleLabel = new Label(subtitle);
            subtitleLabel.AddToClassList("header-subtitle");

            titleGroup.Add(titleLabel);
            titleGroup.Add(subtitleLabel);

            var badgeGroup = new VisualElement();
            badgeGroup.AddToClassList("header-badge-group");

            var versionBadge = new Label($"v{version}");
            versionBadge.AddToClassList("version-badge");
            badgeGroup.Add(versionBadge);

            header.Add(titleGroup);
            header.Add(badgeGroup);

            return header;
        }

        public static VisualElement CreateCard(string title, string subtitle, out VisualElement bodyContainer)
        {
            var card = new VisualElement();
            card.AddToClassList("card");

            var header = new VisualElement();
            header.AddToClassList("card-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("card-title");
            header.Add(titleLabel);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.AddToClassList("card-subtitle");
                header.Add(subtitleLabel);
            }

            bodyContainer = new VisualElement();
            bodyContainer.AddToClassList("card-body");

            card.Add(header);
            card.Add(bodyContainer);

            return card;
        }

        public static VisualElement CreateCard(string title, out VisualElement bodyContainer)
        {
            return CreateCard(title, null, out bodyContainer);
        }

        public static Label CreateBadge(string text, string typeClass = "badge-info")
        {
            var badge = new Label(text);
            badge.AddToClassList("badge");
            badge.AddToClassList(typeClass);
            return badge;
        }

        public static Button CreateButton(string text, string styleClass, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.AddToClassList("btn");
            if (!string.IsNullOrEmpty(styleClass))
            {
                btn.AddToClassList(styleClass);
            }
            return btn;
        }

        public static VisualElement CreateMetricCard(string label, string initialValue, out Label valueLabel)
        {
            var card = new VisualElement();
            card.AddToClassList("metric-card");

            valueLabel = new Label(initialValue);
            valueLabel.AddToClassList("metric-value");

            var labelElement = new Label(label);
            labelElement.AddToClassList("metric-label");

            card.Add(valueLabel);
            card.Add(labelElement);

            return card;
        }
    }
}
