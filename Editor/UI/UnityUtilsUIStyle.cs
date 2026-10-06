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
            var btn = new Button(onClick);
            ApplyIconText(btn, text);
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

        public static void ApplyIconText(Button button, string text)
        {
            for (int i = button.childCount - 1; i >= 0; i--)
            {
                var child = button[i];
                if (child.ClassListContains("wui-btn-icon") || child.ClassListContains("wui-btn-text"))
                    child.RemoveFromHierarchy();
            }

            SplitLeadingIcon(text, out var icon, out var label);

            if (string.IsNullOrEmpty(icon))
            {
                button.text = text;
                return;
            }

            button.text = string.Empty;
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;

            var iconElement = new Label(icon);
            iconElement.AddToClassList("wui-btn-icon");
            iconElement.style.minWidth = 14;
            iconElement.style.marginRight = string.IsNullOrEmpty(label) ? 0 : 6;
            iconElement.style.flexShrink = 0;
            iconElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconElement.pickingMode = PickingMode.Ignore;
            button.Add(iconElement);

            if (!string.IsNullOrEmpty(label))
            {
                var textLabel = new Label(label);
                textLabel.AddToClassList("wui-btn-text");
                textLabel.style.flexShrink = 0;
                textLabel.pickingMode = PickingMode.Ignore;
                button.Add(textLabel);
            }
        }

        public static VisualElement CreateIconLabel(string text)
        {
            SplitLeadingIcon(text, out var icon, out var rest);
            if (string.IsNullOrEmpty(icon))
                return new Label(text);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var iconElement = new Label(icon);
            iconElement.AddToClassList("wui-btn-icon");
            iconElement.style.minWidth = 14;
            iconElement.style.marginRight = string.IsNullOrEmpty(rest) ? 0 : 6;
            iconElement.style.flexShrink = 0;
            iconElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconElement.pickingMode = PickingMode.Ignore;
            row.Add(iconElement);

            var label = new Label(rest);
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            return row;
        }

        internal static void SplitLeadingIcon(string text, out string icon, out string label)
        {
            icon = null;
            label = text;
            if (string.IsNullOrEmpty(text)) return;

            int i = 0;
            while (i < text.Length)
            {
                int codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                    ? char.ConvertToUtf32(text[i], text[i + 1])
                    : text[i];

                if (!IsIconCodePoint(codePoint)) break;
                i += char.IsHighSurrogate(text[i]) ? 2 : 1;
            }

            if (i == 0) return;

            icon = text.Substring(0, i).TrimEnd();
            label = text.Substring(i).TrimStart();
        }

        private static bool IsIconCodePoint(int codePoint) =>
            (codePoint >= 0x2190 && codePoint <= 0x2BFF)
            || (codePoint >= 0x1F000 && codePoint <= 0x1FAFF)
            || codePoint == 0xFE0F
            || codePoint == 0x20E3;
    }
}
