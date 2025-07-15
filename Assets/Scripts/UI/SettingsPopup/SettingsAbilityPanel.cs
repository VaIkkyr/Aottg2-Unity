using Settings;
using System;
using UnityEngine;
using UnityEngine.UI;
using Utility;

namespace UI
{
    class SettingsAbilityPanel: SettingsCategoryPanel
    {
        protected override TextAnchor PanelAlignment => TextAnchor.UpperCenter;
        protected Text _pointsLeftLabel;

        protected GameObject _radiusElement;
        protected GameObject _rangeElement;
        protected GameObject _speedElement;
        protected GameObject _cooldownElement;
        public override void Setup(BasePanel parent = null)
        {
            base.Setup(parent);
            

            
            SettingsPopup settingsPopup = (SettingsPopup)parent;
            string cat = settingsPopup.LocaleCategory;
            string sub = "Ability";
            AbilitySettings settings = SettingsManager.AbilitySettings;
            ElementStyle style = new ElementStyle(titleWidth: 200f, themePanel: ThemePanel);
            ElementFactory.CreateToggleSetting(DoublePanelRight, style, settings.CursorCooldown, UIManager.GetLocale(cat, sub, "CursorCooldown"));
            ElementFactory.CreateToggleSetting(DoublePanelRight, style, settings.ShowBombColors, UIManager.GetLocale(cat, sub, "ShowBombColors"));
            ElementFactory.CreateToggleSetting(DoublePanelRight, style, settings.OldBombEffect, UIManager.GetLocale(cat, sub, "OldBombEffect"));
            
            // Helper to create styled community buttons
            GameObject CreateCommunityButton(Transform parent, string label, string url, int fontSize = 18)
            {
                GameObject button = ElementFactory.CreateDefaultButton(
                    parent,
                    style,
                    label,
                    0f,
                    0f,
                    () => Application.OpenURL(url)
                );
                Text buttonText = button.transform.Find("Text").GetComponent<Text>();
                if (buttonText != null)
                {
                    buttonText.color = new Color(1f, 0.55f, 0f, 1f); // orange
                    buttonText.alignment = TextAnchor.MiddleCenter;
                    buttonText.fontStyle = FontStyle.Bold;
                    buttonText.fontSize = fontSize;
                }
                LayoutElement layout = button.GetComponent<LayoutElement>();
                if (layout != null && buttonText != null)
                {
                    float padW = 10f, padH = 6f;
                    layout.preferredWidth = buttonText.preferredWidth + padW;
                    layout.preferredHeight = buttonText.preferredHeight + padH;
                    layout.minWidth = buttonText.preferredWidth + padW;
                    layout.minHeight = buttonText.preferredHeight + padH;
                }
                Button buttonComponent = button.GetComponent<Button>();
                if (buttonComponent != null)
                {
                    ColorBlock cb = buttonComponent.colors;
                    cb.normalColor = new Color(0.09f, 0.10f, 0.11f, 1f); // #181A1C dark background
                    cb.highlightedColor = new Color(0.15f, 0.17f, 0.19f, 1f); // lighter on hover
                    cb.pressedColor = new Color(0.07f, 0.08f, 0.09f, 1f); // even darker when pressed
                    cb.selectedColor = cb.highlightedColor;
                    cb.disabledColor = new Color(0.2f, 0.22f, 0.24f, 0.5f);
                    buttonComponent.colors = cb;
                }
                return button;
            }

            // Helper to add a vertical spacer to a parent
            void AddSpacer(Transform parent, float height)
            {
                var spacer = new GameObject("Spacer", typeof(RectTransform));
                spacer.transform.SetParent(parent, false);
                var layoutElem = spacer.AddComponent<LayoutElement>();
                layoutElem.minHeight = height;
            }

            // Helper to add the community button group
            void AddCommunityButtonGroup(Transform parent, ElementStyle style)
            {
                var group = new GameObject("CommunityButtonGroup", typeof(RectTransform));
                group.transform.SetParent(parent, false);
                var layout = group.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = 4f;
                layout.padding = new RectOffset(0, 0, 0, 0);

                CreateCommunityButton(group.transform, UIManager.GetLocale(cat, sub, "BombCommunityRanking"), "https://bomb-cards.vercel.app/ranking-cards", 20);
                AddSpacer(group.transform, 8f);

                var labelObj = ElementFactory.CreateDefaultLabel(group.transform, style, "");
                var labelText = labelObj.GetComponent<Text>();
                if (labelText != null)
                {
                    labelText.supportRichText = true;
                    labelText.text = "<color=#FFA500>(↓)</color> <color=#2196F3>COMMUNITY HYPERLINKS</color> <color=#FFA500>(↑)</color>";
                    labelText.fontSize = 16;
                    labelText.alignment = TextAnchor.MiddleCenter;
                    labelText.fontStyle = FontStyle.Bold;
                }
                AddSpacer(group.transform, 8f);

                CreateCommunityButton(group.transform, UIManager.GetLocale(cat, sub, "BombCommunityDiscord"), "https://discord.gg/J2REsMxDYg", 20);
            }

            // Add Bomb Community buttons, slightly smaller and spaced out
            AddSpacer(DoublePanelRight, 8f);
            AddCommunityButtonGroup(DoublePanelRight, style);
            AddSpacer(DoublePanelRight, 8f);
            _pointsLeftLabel = ElementFactory.CreateDefaultLabel(DoublePanelLeft, style, UIManager.GetLocale(cat, sub, "UnusedPoints")).GetComponent<Text>();
            _radiusElement = ElementFactory.CreateIncrementSetting(DoublePanelLeft, style, settings.BombRadius, GetBombStatLabel(cat, sub, "BombRadius", settings.BombRadius.Value, 5.40f, 7.4f, 7f, "m"), onValueChanged: () => OnStatChanged(settings.BombRadius));
            _rangeElement = ElementFactory.CreateIncrementSetting(DoublePanelLeft, style, settings.BombRange, GetBombStatLabel(cat, sub, "BombRange", settings.BombRange.Value, 0f, 4f, 7f, "m"), onValueChanged: () => OnStatChanged(settings.BombRange));
            _speedElement = ElementFactory.CreateIncrementSetting(DoublePanelLeft, style, settings.BombSpeed, GetBombStatLabel(cat, sub, "BombSpeed", settings.BombSpeed.Value, 3f, 10.5f, 10.5f, "k", 100f), onValueChanged: () => OnStatChanged(settings.BombSpeed));
            _cooldownElement = ElementFactory.CreateIncrementSetting(DoublePanelLeft, style, settings.BombCooldown, GetBombStatLabel(cat, sub, "BombCooldown", settings.BombCooldown.Value, 4f, 7f, 7f, "s"), onValueChanged: () => OnStatChanged(settings.BombCooldown));
            ElementFactory.CreateColorSetting(DoublePanelLeft, style, settings.BombColor, UIManager.GetLocale(cat, sub, "BombColor"), UIManager.CurrentMenu.ColorPickPopup);
            

            
            OnStatChanged(settings.BombRadius);
        }

        protected string GetBombStatLabel(string cat, string sub, string statName, int pointsSpent, float oldMinCost, float oldMaxCost, float cutoff, string unit, float divisor = 1f)
        {
            float actualValue;
            float oldCost;
            
            switch (statName)
            {
                case "BombRadius":
                    actualValue = BombUtil.GetBombRadius(pointsSpent, oldMinCost, oldMaxCost, cutoff);
                    oldCost = BombUtil.GetOldRadiusCost(actualValue);
                    break;
                case "BombRange":
                    actualValue = BombUtil.GetBombRange(pointsSpent, oldMinCost, oldMaxCost, cutoff);
                    oldCost = BombUtil.GetOldRangeCost(actualValue);
                    break;
                case "BombSpeed":
                    actualValue = BombUtil.GetBombSpeed(pointsSpent, oldMinCost, oldMaxCost, cutoff);
                    oldCost = BombUtil.GetOldSpeedCost(actualValue);
                    break;
                case "BombCooldown":
                    actualValue = BombUtil.GetBombCooldown(pointsSpent, oldMinCost, oldMaxCost, cutoff);
                    oldCost = BombUtil.GetOldCooldownCost(actualValue);
                    break;
                default:
                    actualValue = 0f;
                    oldCost = 0f;
                    break;
            }
            
            string actualValueStr = (actualValue / divisor).ToString("0.##");
            string oldCostStr = oldCost.ToString("0.##");

            // Format: statName\n(centered combined parentheses)
            string statNameStr = UIManager.GetLocale(cat, sub, statName);
            string combinedValues = $"({oldCostStr}) ({actualValueStr}{unit})";
            
            // Center the combined parentheses line under the stat name
            string centeredValues = CenterString(combinedValues, Math.Max(statNameStr.Length, combinedValues.Length));
            
            return statNameStr + "\n" + centeredValues;
        }

        private string CenterString(string text, int totalWidth)
        {
            if (text.Length >= totalWidth) return text;
            int padding = totalWidth - text.Length;
            int leftPad = padding / 2;
            int rightPad = padding - leftPad;
            return new string(' ', leftPad) + text + new string(' ', rightPad);
        }

        protected void OnStatChanged(IntSetting setting)
        {
            RefreshBombStatLabels();
        }
        
        protected void RefreshBombStatLabels()
        {
            AbilitySettings settings = SettingsManager.AbilitySettings;
            string cat = "SettingsPopup";
            string sub = "Ability";
            int maxPoints = 20;
            int used = settings.BombRadius.Value + settings.BombRange.Value + settings.BombSpeed.Value + settings.BombCooldown.Value;
            int unused = Math.Max(0, maxPoints - used);
            _pointsLeftLabel.text = UIManager.GetLocale(cat, sub, "UnusedPoints") + ": " + unused;
            // Update each bomb stat label with new calculated values
            UpdateElementLabel(_radiusElement, GetBombStatLabel(cat, sub, "BombRadius", settings.BombRadius.Value, 5.40f, 7.4f, 7f, "m"));
            UpdateElementLabel(_rangeElement, GetBombStatLabel(cat, sub, "BombRange", settings.BombRange.Value, 0f, 4f, 7f, "m"));
            UpdateElementLabel(_speedElement, GetBombStatLabel(cat, sub, "BombSpeed", settings.BombSpeed.Value, 3f, 10.5f, 10.5f, "k", 100f));
            UpdateElementLabel(_cooldownElement, GetBombStatLabel(cat, sub, "BombCooldown", settings.BombCooldown.Value, 4f, 7f, 7f, "s"));
        }
        
        protected void UpdateElementLabel(GameObject element, string newText)
        {
            if (element != null)
            {
                Text labelText = element.GetComponentInChildren<Text>();
                if (labelText != null)
                {
                    labelText.text = newText;
                }
            }
        }
    }
}
