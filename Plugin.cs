using System;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Ink.Runtime;
using Date_Everything.Scripts.Ink;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace PreviewEverything;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static Story story;
    public static bool IsPreviewMode = false;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} patches done: {harmony.GetPatchedMethods().ToList().Count} methods");
    }

    public static void Log(string message)
    {
        Logger.LogInfo(message);
    }

    public static void CreateStoryFromInkJSON(string inkJSON)
    {
        if (string.IsNullOrEmpty(inkJSON))
        {
            Logger.LogError("Ink JSON is null or empty.");
            return;
        }

        try
        {
            story = new Story(inkJSON);
            Logger.LogInfo("Story created successfully from Ink JSON.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create story from Ink JSON: {ex.Message}");
        }
    }

    public static void SyncStoryState(Story mainStory)
    {
        if (story == null || mainStory == null)
        {
            Logger.LogWarning("Cannot sync story state - one of the stories is null");
            return;
        }
        
        try
        {
            string mainStoryState = mainStory.state.ToJson();
            story.state.LoadJson(mainStoryState);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to sync story state: {ex.Message}");
        }
    }

    public static List<string> GetChoiceEffects(Choice choice)
    {
        List<string> effects = [];

        if (story == null)
        {
            return effects;
        }

        try
        {
            string savedState = story.state.ToJson();

            var initialVariables = new Dictionary<string, object>();
            foreach (string varName in story.variablesState)
            {
                initialVariables[varName] = story.variablesState[varName];
            }

            story.ChooseChoiceIndex(choice.index);

            int maxLines = 50;
            int lineCount = 0;
            bool didAdvance = false;

            while (story.canContinue && lineCount < maxLines)
            {
                string line = story.Continue();
                if (story.currentTags != null)
                {
                    foreach (string tag in story.currentTags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            if (tag == "home" && !didAdvance)
                            {
                                effects.Add("End of dialogue");
                            }
                        }
                    }
                }

                lineCount++;

                if (story.currentChoices.Count > 1)
                {
                    break;
                }
                else if (story.currentChoices.Count == 1)
                {
                    didAdvance = true;
                    story.ChooseChoiceIndex(0);
                }
            }

            foreach (string varName in story.variablesState)
            {
                if (varName == "money")
                {
                    continue;
                }
                var currentValue = story.variablesState[varName];
                string displayName = string.Join(" ", varName.Split('_').Select(word => char.ToUpper(word[0]) + word.Substring(1)));
                if (initialVariables.ContainsKey(varName))
                {
                    var initialValue = initialVariables[varName];

                    if (!object.Equals(currentValue, initialValue))
                    {
                        if ((currentValue is int || currentValue is float) && (initialValue is int || initialValue is float))
                        {
                            var difference = initialValue is int v
                                ? (int)currentValue - v
                                : (float)currentValue - (float)initialValue;
                            if (difference > 0)
                            {
                                effects.Add($"{displayName} +{difference} ({initialValue} -> {currentValue})");
                            }
                            else if (difference < 0)
                            {
                                effects.Add($"{displayName} {difference} ({initialValue} -> {currentValue})");
                            }
                        }
                        else if (varName.EndsWith("_ending"))
                        {
                            string endingName = displayName.Replace("Ending", "").Trim();
                            effects.Add($"Ending {endingName}: {currentValue}");
                        }
                        else
                        {
                            effects.Add($"{displayName}: {currentValue}");
                        }
                    }
                }
                else
                {
                    if (varName.EndsWith("_ending"))
                    {
                        string endingName = displayName.Replace("Ending", "").Trim();
                        effects.Add($"Ending {endingName}: {currentValue}");
                    }
                    else
                    {
                        effects.Add($"{displayName}: {currentValue}");
                    }
                }
            }

            story.state.LoadJson(savedState);

            return effects;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error previewing choice: {ex.Message}");
            return effects;
        }
    }

    public static string PreviewChoice(Choice choice)
    {
        if (story == null)
        {
            return "Plugin story not initialized";
        }

        try
        {
            string savedState = story.state.ToJson();

            var result = new System.Text.StringBuilder();
            var textLines = new List<string>();
            var tags = new List<string>();
            var variableChanges = new Dictionary<string, object>();

            var initialVariables = new Dictionary<string, object>();
            foreach (string varName in story.variablesState)
            {
                initialVariables[varName] = story.variablesState[varName];
            }

            story.ChooseChoiceIndex(choice.index);

            int maxLines = 50;
            int lineCount = 0;
            bool skipChoices = false;

            while (story.canContinue && lineCount < maxLines)
            {
                string line = story.Continue();

                if (!string.IsNullOrWhiteSpace(line.Trim()))
                {
                    if (line.Trim() == "You are in your own house. Everything is alive and sexy. What do you do?")
                    {
                        skipChoices = true;
                        continue;
                    }
                    textLines.Add(line.Trim());
                }

                if (story.currentTags != null)
                {
                    foreach (string tag in story.currentTags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            if (tag.StartsWith("audio_name ") || tag.StartsWith("pose ") || tag.StartsWith("emote ")
                            || tag.StartsWith("music_") || tag.StartsWith("location ") || tag.StartsWith("dex ")
                            || tag.StartsWith("collectable_release ") || tag.StartsWith("StatCheck "))
                            {
                                continue;
                            }
                            tags.Add(tag.Trim());
                        }
                    }
                }

                lineCount++;

                if (story.currentChoices.Count > 1)
                {
                    break;
                }
                else if (story.currentChoices.Count == 1)
                {
                    story.ChooseChoiceIndex(0);
                }
            }

            foreach (string varName in story.variablesState)
            {
                if (varName == "money")
                {
                    continue;
                }
                var currentValue = story.variablesState[varName];
                if (initialVariables.ContainsKey(varName))
                {
                    var initialValue = initialVariables[varName];
                    if (!object.Equals(currentValue, initialValue))
                    {
                        variableChanges[varName] = new { from = initialValue, to = currentValue };
                    }
                }
                else
                {
                    variableChanges[varName] = new { from = "(new)", to = currentValue };
                }
            }

            if (textLines.Count > 0)
            {
                foreach (string text in textLines)
                {
                    result.AppendLine($"{text}");
                }
                result.AppendLine();
            }

            // if (tags.Count > 0)
            // {
            //     result.AppendLine("Commands/Tags:");
            //     foreach (string tag in tags)
            //     {
            //         result.AppendLine($"  {tag}");
            //     }
            //     result.AppendLine();
            // }

            if (variableChanges.Count > 0)
            {
                result.AppendLine("Variable Changes:");
                foreach (var change in variableChanges)
                {
                    result.AppendLine($"  {change.Key}: {change.Value}");
                }
                result.AppendLine();
            }

            if (!tags.Contains("home") && !skipChoices)
            {
                if (story.currentChoices.Count > 0)
                {
                    result.AppendLine($"Leads to {story.currentChoices.Count} more choice(s)");

                    if (story.currentChoices.Count > 5)
                    {
                        result.AppendLine("Too many choices to display, showing first 5:");
                    }
                    int maxChoicesToShow = Math.Min(story.currentChoices.Count, 5);
                    for (int i = 0; i < maxChoicesToShow; i++)
                    {
                        var nextChoice = story.currentChoices[i];
                        result.AppendLine($"   • {nextChoice.text}");
                    }
                }
                else if (!story.canContinue)
                {
                    result.AppendLine("Reaches end of story/section");
                }
            }
            else
            {
                result.AppendLine("Ends dialogue");
            }
            if (lineCount >= maxLines)
            {
                result.AppendLine("Preview truncated (too much content)");
            }

            story.state.LoadJson(savedState);

            return result.ToString();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error previewing choice: {ex.Message}");
            return $"Error previewing choice: {ex.Message}";
        }
    }
}

public class ChoiceTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private static GameObject tooltipObject;
    private static TextMeshProUGUI tooltipText;
    private static RectTransform tooltipRect;
    private static Canvas tooltipCanvas;
    
    public string effectsContent;
    public string previewContent;
    public bool isInputHandler = false;
    
    private void Start()
    {
        CreateTooltipSystem();
    }

    private void Update()
    {
        if (!isInputHandler)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.CapsLock))
        {
            Plugin.IsPreviewMode = !Plugin.IsPreviewMode;
            Plugin.Log($"Preview mode toggled: {Plugin.IsPreviewMode}");
        }
    }
    
    private static void CreateTooltipSystem()
    {
        if (tooltipObject != null) return;

        GameObject canvasGO = new GameObject("TooltipCanvas");
        tooltipCanvas = canvasGO.AddComponent<Canvas>();
        tooltipCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        tooltipCanvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasGO.AddComponent<GraphicRaycaster>();

        tooltipObject = new GameObject("Tooltip");
        tooltipObject.transform.SetParent(canvasGO.transform, false);

        tooltipRect = tooltipObject.AddComponent<RectTransform>();
        tooltipRect.sizeDelta = new Vector2(300, 200);

        Image background = tooltipObject.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

        Outline outline = tooltipObject.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(1, 1);

        GameObject textGO = new GameObject("TooltipText");
        textGO.transform.SetParent(tooltipObject.transform, false);

        tooltipText = textGO.AddComponent<TextMeshProUGUI>();
        tooltipText.text = "";
        tooltipText.fontSize = 14;
        tooltipText.color = Color.white;
        tooltipText.alignment = TextAlignmentOptions.TopLeft;
        tooltipText.margin = new Vector4(10, 10, 10, 10);

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        tooltipObject.SetActive(false);

        DontDestroyOnLoad(canvasGO);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        string contentToShow = null;

        if (Plugin.IsPreviewMode && !string.IsNullOrEmpty(previewContent))
        {
            contentToShow = previewContent;
        }
        else if (!Plugin.IsPreviewMode && !string.IsNullOrEmpty(effectsContent))
        {
            contentToShow = effectsContent;
        }

        if (!string.IsNullOrEmpty(contentToShow))
        {
            ShowTooltip(contentToShow, eventData.position);
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        HideTooltip();
    }
    
    private static void ShowTooltip(string content, Vector2 mousePosition)
    {
        if (tooltipObject == null) return;
        
        tooltipText.text = content;
        tooltipObject.SetActive(true);

        tooltipText.ForceMeshUpdate();
        Vector2 textSize = new Vector2(tooltipText.preferredWidth, tooltipText.preferredHeight);
        textSize.x = Mathf.Clamp(textSize.x + 40, 150, 500);

        tooltipRect.sizeDelta = textSize;

        Vector2 adjustedMousePosition = new Vector2(mousePosition.x + 10, mousePosition.y - 10);
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            tooltipCanvas.transform as RectTransform,
            adjustedMousePosition,
            tooltipCanvas.worldCamera,
            out localPoint);

        Vector2 pos = localPoint;
        float halfWidth = textSize.x * 0.5f;
        float halfHeight = textSize.y * 0.5f;
        
        if (pos.x + halfWidth > Screen.width * 0.5f)
            pos.x -= halfWidth + 20;
        else
            pos.x += halfWidth + 20;
            
        if (pos.y - halfHeight < -Screen.height * 0.5f)
            pos.y += halfHeight + 20;
        else
            pos.y -= halfHeight;
        
        tooltipRect.localPosition = pos;
    }
    
    private static void HideTooltip()
    {
        if (tooltipObject != null)
            tooltipObject.SetActive(false);
    }
}

[HarmonyPatch]
public class PluginPatches
{
    [HarmonyPatch(typeof(InkStoryProvider), "BeginLoadStory")]
    [HarmonyPrefix]
    public static void BeginLoadStory(InkStoryProvider __instance)
    {
        try
        {
            if (Plugin.story != null)
            {
                Plugin.Log("Story already loaded, skipping BeginLoadStory.");
                return;
            }

            Plugin.Log("BeginLoadStory called");
            var inkJSONAssetField = typeof(InkStoryProvider).GetField("inkJSONAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (inkJSONAssetField != null)
            {
                TextAsset inkJSONAsset = inkJSONAssetField.GetValue(__instance) as TextAsset;
                if (inkJSONAsset != null)
                {
                    Plugin.Log($"Ink JSON Asset loaded: {inkJSONAsset.name}");
                    Plugin.CreateStoryFromInkJSON(inkJSONAsset.text);
                }
                else
                {
                    Plugin.Log("Ink JSON Asset is null");
                }
            }
            else
            {
                Plugin.Log("inkJSONAsset field not found");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Error in BeginLoadStory: {ex.Message}");
            Plugin.Logger.LogError(ex.StackTrace);
        }
    }

    [HarmonyPatch(typeof(TalkingUI), "RefreshViewStep3AfterParseTag")]
    [HarmonyPostfix]
    public static void RefreshViewStep3AfterParseTag(TalkingUI __instance)
    {
        try
        {
            if (Singleton<InkController>.Instance.ShouldGoHome())
            {
                return;
            }

            if (Singleton<InkController>.Instance.story.currentChoices.Count > 0)
            {
                var mainStory = Singleton<InkController>.Instance.story;

                Plugin.SyncStoryState(mainStory);

                var choicesButtonsField = typeof(TalkingUI).GetField("choicesButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (choicesButtonsField != null)
                {
                    List<Button> choicesButtons = choicesButtonsField.GetValue(__instance) as List<Button>;
                    if (choicesButtons != null)
                    {
                        int idx = 0;
                        bool hasInputHandler = false;
                        foreach (Choice choice in mainStory.currentChoices)
                        {
                            string preview = Plugin.PreviewChoice(choice);

                            if (choicesButtons[idx] != null)
                            {
                                var button = choicesButtons[idx];
                                DialogueButton dialogueButton = button.GetComponent<DialogueButton>();
                                var buttonText = dialogueButton.GetComponentInChildren<TMPro.TextMeshProUGUI>();

                                if (buttonText == null)
                                {
                                    continue;
                                }

                                List<string> effects = Plugin.GetChoiceEffects(choice);

                                if (effects.Count > 0)
                                {
                                    if (effects.Count == 1 && effects.Contains("End of dialogue"))
                                    {
                                        if (!buttonText.text.Contains("<i>*end*</i>"))
                                        {
                                            buttonText.text = $"{buttonText.text} <i>*end*</i>";
                                        }

                                        var tooltip = dialogueButton.gameObject.GetComponent<ChoiceTooltip>();
                                        if (tooltip == null)
                                        {
                                            tooltip = dialogueButton.gameObject.AddComponent<ChoiceTooltip>();
                                        }
                                        tooltip.effectsContent = "";
                                        tooltip.previewContent = preview;
                                        if (!hasInputHandler)
                                        {
                                            tooltip.isInputHandler = true;
                                            hasInputHandler = true;
                                        }
                                    }
                                    else
                                    {
                                        var tooltip = dialogueButton.gameObject.GetComponent<ChoiceTooltip>();
                                        if (tooltip == null)
                                        {
                                            tooltip = dialogueButton.gameObject.AddComponent<ChoiceTooltip>();
                                        }

                                        bool hasEnding = effects.Any(e => e.StartsWith("Ending "));
                                        string effectsContent = string.Join("\n", effects);
                                        tooltip.effectsContent = effectsContent;
                                        tooltip.previewContent = preview;

                                        string icon = "☆";

                                        if (hasEnding)
                                        {
                                            icon = "★";
                                        }

                                        if (!buttonText.text.Contains(icon))
                                        {
                                            buttonText.text = $"{buttonText.text} {icon}";
                                        }

                                        if (!hasInputHandler)
                                        {
                                            tooltip.isInputHandler = true;
                                            hasInputHandler = true;
                                        }
                                    }
                                }
                                else
                                {
                                    var tooltip = button.gameObject.GetComponent<ChoiceTooltip>();
                                    if (tooltip == null)
                                    {
                                        tooltip = button.gameObject.AddComponent<ChoiceTooltip>();
                                    }

                                    tooltip.effectsContent = "";
                                    tooltip.previewContent = preview;
                                    if (!hasInputHandler)
                                    {
                                        tooltip.isInputHandler = true;
                                        hasInputHandler = true;
                                    }
                                }
                            }
                            idx++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Error in RefreshViewStep3AfterParseTag: {ex.Message}");
            Plugin.Logger.LogError(ex.StackTrace);
        }
    }
}
