using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// UI Toolkit view presenting the consolidated Project Cleanup Suite with
    /// one-click scanning, metric cards, contextual diagnostics, recommended solutions,
    /// interactive pingable asset lists, and before/after improvement panels.
    /// </summary>
    public class CleanupSuiteView : VisualElement
    {
        private Label _audioMetric;
        private Label _scriptsMetric;
        private Label _tmpMetric;
        private Label _statusLabel;

        // Dynamic result panels under each card
        private VisualElement _audioResultPanel;
        private VisualElement _scriptsResultPanel;
        private VisualElement _tmpResultPanel;
        private VisualElement _animResultPanel;
        private VisualElement _legacyResultPanel;
        private VisualElement _iapResultPanel;

        // Last scan counts for diffing
        private int _lastAudioCount;
        private int _lastScriptsCount;
        private int _lastTmpCount;
        private int _lastAnimCount;
        private int _lastLegacyCount;
        private int _lastIapCount;

        public CleanupSuiteView()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            // 1. Metrics Overview
            var metricsRow = new VisualElement();
            metricsRow.AddToClassList("metrics-row");

            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Audio Duplicates", "-", out _audioMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Missing Scripts", "-", out _scriptsMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("TMP Redundancies", "-", out _tmpMetric));
            scroll.Add(metricsRow);

            // 2. Scan All Banner
            var scanCard = UnityUtilsUIStyle.CreateCard(
                "Full Project Audit",
                "Scan all scenes and prefabs across all diagnostic cleaner modules.",
                out var scanBody);

            var scanBtn = UnityUtilsUIStyle.CreateButton("Run Full Project Scan", "btn-primary", RunFullScan);
            scanBtn.style.alignSelf = Align.FlexStart;
            scanBody.Add(scanBtn);
            scroll.Add(scanCard);

            // 3. Audio Listeners Card
            var audioCard = UnityUtilsUIStyle.CreateCard(
                "Audio Listeners",
                "Detects and cleans duplicate AudioListeners across scenes and prefabs.",
                out var audioBody);

            var audioBar = new VisualElement();
            audioBar.AddToClassList("action-toolbar");
            audioBar.style.marginTop = 0;
            audioBar.style.borderTopWidth = 0;

            audioBar.Add(CreateActionButton("Scan Project", "btn-secondary", "AudioListener scan", RunAudioScan));
            audioBar.Add(CreateActionButton("Fix All (Scenes & Prefabs)", "btn-primary", "AudioListener cleanup", RunAudioFixAll));
            audioBar.Add(CreateActionButton("Remove Non-Bootstrap Listeners", "btn-danger", "AudioListener removal", RunAudioRemoveNonBootstrap));

            audioBody.Add(audioBar);
            _audioResultPanel = new VisualElement();
            _audioResultPanel.AddToClassList("scan-result-panel");
            _audioResultPanel.style.display = DisplayStyle.None;
            audioBody.Add(_audioResultPanel);
            scroll.Add(audioCard);

            // 4. Missing Scripts Card
            var scriptCard = UnityUtilsUIStyle.CreateCard(
                "Missing Scripts",
                "Finds and removes missing MonoBehaviour script components from GameObjects.",
                out var scriptBody);

            var scriptBar = new VisualElement();
            scriptBar.AddToClassList("action-toolbar");
            scriptBar.style.marginTop = 0;
            scriptBar.style.borderTopWidth = 0;

            scriptBar.Add(CreateActionButton("Scan Project", "btn-secondary", "Missing scripts scan", RunScriptsScan));
            scriptBar.Add(CreateActionButton("Fix All (Scenes & Prefabs)", "btn-primary", "Missing scripts cleanup", RunScriptsFixAll));

            scriptBody.Add(scriptBar);
            _scriptsResultPanel = new VisualElement();
            _scriptsResultPanel.AddToClassList("scan-result-panel");
            _scriptsResultPanel.style.display = DisplayStyle.None;
            scriptBody.Add(_scriptsResultPanel);
            scroll.Add(scriptCard);

            // 5. TextMeshPro Cleanup Card
            var tmpCard = UnityUtilsUIStyle.CreateCard(
                "TextMesh Pro Cleaners",
                "Cleans redundant CanvasRenderers on world-space text and removes obsolete TextContainers.",
                out var tmpBody);

            var tmpBar = new VisualElement();
            tmpBar.AddToClassList("action-toolbar");
            tmpBar.style.marginTop = 0;
            tmpBar.style.borderTopWidth = 0;

            tmpBar.Add(CreateActionButton("Scan TMP Issues", "btn-secondary", "TMP scan", RunTmpScan));
            tmpBar.Add(CreateActionButton("Fix CanvasRenderers", "btn-primary", "CanvasRenderer cleanup", RunTmpCanvasFix));
            tmpBar.Add(CreateActionButton("Fix TextContainers", "btn-primary", "TextContainer cleanup", RunTmpContainerFix));

            tmpBody.Add(tmpBar);
            _tmpResultPanel = new VisualElement();
            _tmpResultPanel.AddToClassList("scan-result-panel");
            _tmpResultPanel.style.display = DisplayStyle.None;
            tmpBody.Add(_tmpResultPanel);
            scroll.Add(tmpCard);

            // 6. Animator Transitions Card
            var animCard = UnityUtilsUIStyle.CreateCard(
                "Animator Transitions",
                "Audits and repairs invalid animator state transitions without conditions.",
                out var animBody);

            var animBar = new VisualElement();
            animBar.AddToClassList("action-toolbar");
            animBar.style.marginTop = 0;
            animBar.style.borderTopWidth = 0;

            animBar.Add(CreateActionButton("Audit Transitions", "btn-secondary", "Animator transition audit", RunAnimScan));
            animBar.Add(CreateActionButton("Fix: Enable Exit Time", "btn-primary", "Animator transition fix", () => RunAnimFix(AnimatorTransitionCleaner.FixMode.EnableExitTime)));
            animBar.Add(CreateActionButton("Fix: Remove Invalid", "btn-danger", "Animator transition removal", () => RunAnimFix(AnimatorTransitionCleaner.FixMode.Remove)));

            animBody.Add(animBar);
            _animResultPanel = new VisualElement();
            _animResultPanel.AddToClassList("scan-result-panel");
            _animResultPanel.style.display = DisplayStyle.None;
            animBody.Add(_animResultPanel);
            scroll.Add(animCard);

            // 7. Legacy Components & Modernize Card
            var legacyCard = UnityUtilsUIStyle.CreateCard(
                "Legacy Component Modernizer",
                "Re-saves all scenes and prefabs to reserialize assets with the current Unity version format.",
                out var legacyBody);

            var legacyBar = new VisualElement();
            legacyBar.AddToClassList("action-toolbar");
            legacyBar.style.marginTop = 0;
            legacyBar.style.borderTopWidth = 0;

            legacyBar.Add(CreateActionButton("Scan Project", "btn-secondary", "Legacy components scan", RunLegacyScan));
            legacyBar.Add(CreateActionButton("Modernize & Resave Project", "btn-secondary", "Project modernize", RunLegacyResaveAll));
            legacyBar.Add(CreateActionButton("Resave Legacy Plugin Meta", "btn-primary", "Legacy plugin importer resave", RunLegacyResavePlugins));

            legacyBody.Add(legacyBar);
            _legacyResultPanel = new VisualElement();
            _legacyResultPanel.AddToClassList("scan-result-panel");
            _legacyResultPanel.style.display = DisplayStyle.None;
            legacyBody.Add(_legacyResultPanel);
            scroll.Add(legacyCard);

            // 8. Unused IAP Buttons Card
            var iapCard = UnityUtilsUIStyle.CreateCard(
                "Unused IAP Buttons",
                "Detects empty and unused Codeless IAP buttons.",
                out var iapBody);

#if !WAGENHEIMER_UNITYUTILS_IAP
            var iapNotice = new Label("IAP support is not enabled in this project (com.unity.purchasing not present).");
            iapNotice.AddToClassList("card-subtitle");
            iapBody.Add(iapNotice);
#else
            var iapBar = new VisualElement();
            iapBar.AddToClassList("action-toolbar");
            iapBar.style.marginTop = 0;
            iapBar.style.borderTopWidth = 0;

            iapBar.Add(CreateActionButton("Scan IAP Buttons", "btn-secondary", "IAP button scan", RunIapScan));
            iapBar.Add(CreateActionButton("Fix All Unused", "btn-primary", "IAP button cleanup", RunIapFixAll));

            iapBody.Add(iapBar);
            _iapResultPanel = new VisualElement();
            _iapResultPanel.AddToClassList("scan-result-panel");
            _iapResultPanel.style.display = DisplayStyle.None;
            iapBody.Add(_iapResultPanel);
#endif
            scroll.Add(iapCard);

            // Status label
            _statusLabel = new Label("Ready. Click 'Run Full Project Scan' to check all cleaners.");
            _statusLabel.AddToClassList("status-box");
            scroll.Add(_statusLabel);

            Add(scroll);
        }

        #region Scan Operations

        private void RunFullScan()
        {
            RunGuarded("Full project scan", () =>
            {
                RunAudioScan();
                RunScriptsScan();
                RunTmpScan();
                RunAnimScan();
                RunLegacyScan();
#if WAGENHEIMER_UNITYUTILS_IAP
                RunIapScan();
#endif
                SetStatus("Auditoria completa do projeto concluída. Verifique os resultados e diagnósticos abaixo de cada módulo.");
            });
        }

        private void RunAudioScan()
        {
            var scan = AudioListenerCleaner.ScanProject();
            _lastAudioCount = scan.DuplicateCount;
            _audioMetric.text = scan.DuplicateCount.ToString();

            RenderScanResults(
                _audioResultPanel,
                issueCount: scan.DuplicateCount,
                successMsg: "Nenhum AudioListener duplicado encontrado. Suas cenas e prefabs estão com áudio íntegro!",
                diagTitle: "Múltiplos AudioListeners Detectados",
                diagDesc: $"Foram encontrados {scan.DuplicateCount} AudioListener(s) duplicados em {scan.AffectedScenesCount} cena(s) e {scan.AffectedPrefabsCount} prefab(s). Ter mais de um listener em cena causa avisos recorrentes no console e divide a espacialização do áudio.",
                recDesc: "Recomendação: Mantenha apenas o AudioListener principal (na cena de inicialização/câmera principal) e remova os duplicados nas demais cenas e prefabs.",
                fixBtnText: "Corrigir Agora (Remover Duplicados)",
                fixAction: RunAudioFixAll,
                items: scan.Items);

            SetStatus($"AudioListeners scan: {scan.DuplicateCount} duplicados em {scan.AffectedScenesCount} cena(s) e {scan.AffectedPrefabsCount} prefab(s).");
        }

        private void RunScriptsScan()
        {
            var scan = MissingScriptCleaner.ScanProject();
            _lastScriptsCount = scan.MissingScriptCount;
            _scriptsMetric.text = scan.MissingScriptCount.ToString();

            RenderScanResults(
                _scriptsResultPanel,
                issueCount: scan.MissingScriptCount,
                successMsg: "Nenhum script ausente encontrado. Todos os componentes MonoBehaviour estão íntegros!",
                diagTitle: "Componentes com Script Ausente Detectados",
                diagDesc: $"Foram encontrados {scan.MissingScriptCount} componente(s) MonoBehaviour com script ausente em {scan.AffectedScenesCount} cena(s) e {scan.AffectedPrefabsCount} prefab(s). Scripts ausentes quebram referências, degradam a performance de deserialização e geram warnings persistentes de 'The referenced script on this Behaviour is missing!'.",
                recDesc: "Recomendação: Remova as entradas órfãs de MonoBehaviours corrompidos ou desinstalados para limpar a serialização.",
                fixBtnText: "Remover Todos os Scripts Ausentes",
                fixAction: RunScriptsFixAll,
                items: scan.Items);

            SetStatus($"Missing scripts scan: {scan.MissingScriptCount} scripts ausentes em {scan.AffectedScenesCount} cena(s) e {scan.AffectedPrefabsCount} prefab(s).");
        }

        private void RunTmpScan()
        {
            var rScan = TMPCanvasRendererCleaner.ScanProject();
            var cScan = TMPTextContainerCleaner.ScanProject();
            var total = rScan.RedundantRendererCount + cScan.ObsoleteContainerCount;
            _lastTmpCount = total;
            _tmpMetric.text = total.ToString();

            var combinedItems = new List<CleanupIssueItem>();
            if (rScan.Items != null) combinedItems.AddRange(rScan.Items);
            if (cScan.Items != null) combinedItems.AddRange(cScan.Items);

            RenderScanResults(
                _tmpResultPanel,
                issueCount: total,
                successMsg: "Nenhuma redundância de TextMeshPro encontrada. Objetos 3D e UI de texto estão limpos!",
                diagTitle: "Componentes Redundantes / Obsoletos de TextMeshPro",
                diagDesc: $"Detectados {rScan.RedundantRendererCount} CanvasRenderer(s) desnecessários em textos de world-space e {cScan.ObsoleteContainerCount} TextContainer(s) legados. O TextMeshPro 3D não necessita de CanvasRenderer (gerando warnings em tempo de execução) e o TextContainer é obsoleto nas versões atuais.",
                recDesc: "Recomendação: Remova com segurança os CanvasRenderers redundantes e TextContainers legados.",
                fixBtnText: "Corrigir Todas as Redundâncias TMP",
                fixAction: () =>
                {
                    RunTmpCanvasFix();
                    RunTmpContainerFix();
                },
                items: combinedItems);

            SetStatus($"TMP scan: {rScan.RedundantRendererCount} CanvasRenderers redundantes, {cScan.ObsoleteContainerCount} TextContainers obsoletos.");
        }

        private void RunAnimScan()
        {
            var scan = AnimatorTransitionCleaner.ScanProject();
            _lastAnimCount = scan.InvalidTransitionCount;

            RenderScanResults(
                _animResultPanel,
                issueCount: scan.InvalidTransitionCount,
                successMsg: "Todas as transições de Animator Controllers estão íntegras e saudáveis!",
                diagTitle: "Transições de Animator sem Condição nem Exit Time",
                diagDesc: $"Foram encontradas {scan.InvalidTransitionCount} transição(ões) de Animator inválidas em {scan.AffectedControllersCount} controller(s). Transições AnyState sem Exit Time e sem condição nunca são disparadas pelo motor de animação e causam spam de warning no console.",
                recDesc: "Recomendação: Ative 'Has Exit Time' com exitTime = 1.0 para transicionar normalmente após o término da animação.",
                fixBtnText: "Habilitar Exit Time (Recomendado)",
                fixAction: () => RunAnimFix(AnimatorTransitionCleaner.FixMode.EnableExitTime),
                items: scan.Items);

            SetStatus($"Animator scan: {scan.InvalidTransitionCount} transição(ões) inválidas em {scan.AffectedControllersCount} controller(s).");
        }

        private void RunLegacyScan()
        {
            var scan = LegacyComponentCleaner.ScanProject();
            _lastLegacyCount = scan.LegacyPluginCount;

            RenderScanResults(
                _legacyResultPanel,
                issueCount: scan.LegacyPluginCount,
                successMsg: "Todos os metadados de plugins estão na versão 2 (modernos) e o projeto está atualizado!",
                diagTitle: "Metadados de Plugins na Versão Legada (Versão 1)",
                diagDesc: $"Foram encontrados {scan.LegacyPluginCount} arquivo(s) .meta de plugins legados onde PluginImporter está na versão 1. O Unity 6 exige versão 2 e alerta em cada compilação: 'contains a PluginImporter object at version 1, below the supported minimum (2)'.",
                recDesc: "Recomendação: Re-salve os importadores dos plugins legados para elevar a serialização para a versão 2 sem alterar os binários.",
                fixBtnText: "Re-salvar Metas dos Plugins Legados",
                fixAction: RunLegacyResavePlugins,
                items: scan.Items);

            SetStatus($"Legacy scan: {scan.LegacyPluginCount} plugin meta(s) legados encontrados no projeto.");
        }

#if WAGENHEIMER_UNITYUTILS_IAP
        private void RunIapScan()
        {
            var scan = UnusedIAPButtonCleaner.ScanProject();
            _lastIapCount = scan.UnusedComponentCount;

            RenderScanResults(
                _iapResultPanel,
                issueCount: scan.UnusedComponentCount,
                successMsg: "Nenhum botão IAP não utilizado encontrado!",
                diagTitle: "Botões Codeless IAP Sem Eventos Configurados",
                diagDesc: $"Foram encontrados {scan.UnusedComponentCount} componente(s) IAPButton inativos/não utilizados. Componentes IAPButton sem eventos disparam listeners automáticos em background que poluem logs e consomem ciclos de verificação.",
                recDesc: "Recomendação: Remova componentes IAPButton vazios ou não configurados.",
                fixBtnText: "Remover Botões IAP Não Utilizados",
                fixAction: RunIapFixAll,
                items: scan.Items);

            SetStatus($"IAP scan: {scan.UnusedComponentCount} botões IAP sem uso detectados.");
        }
#endif

        #endregion

        #region Fix Operations

        private void RunAudioFixAll()
        {
            var before = _lastAudioCount;
            var sceneRes = AudioListenerCleaner.CleanAllScenes(removeAllNonPersistent: false);
            var prefabRes = AudioListenerCleaner.CleanAllPrefabs(removeAllNonPersistent: false);
            var total = sceneRes.DuplicatesRemoved + prefabRes.DuplicatesRemoved;
            _audioMetric.text = "0";

            var allMods = new List<CleanupModificationRecord>();
            if (sceneRes.Modifications != null) allMods.AddRange(sceneRes.Modifications);
            if (prefabRes.Modifications != null) allMods.AddRange(prefabRes.Modifications);

            RenderDiffCard(
                _audioResultPanel,
                title: "✓ AudioListeners Duplicados Removidos",
                beforeCount: before > 0 ? before : total,
                afterCount: 0,
                benefitDesc: $"Removidos {total} AudioListener(s) duplicados em {sceneRes.ScenesModified} cena(s) e {prefabRes.PrefabsModified} prefab(s). Conflitos de áudio eliminados.",
                modifications: allMods);

            SetStatus($"Removidos {total} AudioListener(s) duplicados ({sceneRes.ScenesModified} cena(s), {prefabRes.PrefabsModified} prefab(s)).");
        }

        private void RunAudioRemoveNonBootstrap()
        {
            var before = _lastAudioCount;
            AudioListenerCleaner.RemoveAllNonBootstrapListeners();
            _audioMetric.text = "0";

            RenderDiffCard(
                _audioResultPanel,
                title: "✓ Listeners Não-Bootstrap Removidos",
                beforeCount: before,
                afterCount: 0,
                benefitDesc: "Todos os AudioListeners fora da cena de bootstrap foram limpos com sucesso. Áudio centralizado no Bootstrap.",
                modifications: null);

            SetStatus("Removidos todos os AudioListeners não pertencentes à cena de bootstrap.");
        }

        private void RunScriptsFixAll()
        {
            var before = _lastScriptsCount;
            var sceneRes = MissingScriptCleaner.CleanAllScenes();
            var prefabRes = MissingScriptCleaner.CleanAllPrefabs();
            var total = sceneRes.MissingScriptsRemoved + prefabRes.MissingScriptsRemoved;
            _scriptsMetric.text = "0";

            var allMods = new List<CleanupModificationRecord>();
            if (sceneRes.Modifications != null) allMods.AddRange(sceneRes.Modifications);
            if (prefabRes.Modifications != null) allMods.AddRange(prefabRes.Modifications);

            RenderDiffCard(
                _scriptsResultPanel,
                title: "✓ Scripts Ausentes Removidos com Sucesso",
                beforeCount: before > 0 ? before : total,
                afterCount: 0,
                benefitDesc: $"Removidos {total} componentes MonoBehaviour com script ausente em {sceneRes.ScenesModified} cena(s) e {prefabRes.PrefabsModified} prefab(s). Serialização limpa e warnings de console suprimidos.",
                modifications: allMods);

            SetStatus($"Removidos {total} script(s) ausente(s) ({sceneRes.ScenesModified} cena(s), {prefabRes.PrefabsModified} prefab(s)).");
        }

        private void RunTmpCanvasFix()
        {
            var before = _lastTmpCount;
            var res = TMPCanvasRendererCleaner.CleanAll();
            _tmpMetric.text = "0";

            RenderDiffCard(
                _tmpResultPanel,
                title: "✓ CanvasRenderers Redundantes Removidos",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: 0,
                benefitDesc: $"Removidos {res.ObjectsFixed} CanvasRenderer(s) redundantes em textos 3D ({res.ScenesChanged} cena(s), {res.PrefabsChanged} prefab(s)). Warnings de 'Please remove the CanvasRenderer' suprimidos.",
                modifications: res.Modifications);

            SetStatus($"Corrigidos {res.ObjectsFixed} CanvasRenderer(s) redundantes em TextMeshPro 3D.");
        }

        private void RunTmpContainerFix()
        {
            var before = _lastTmpCount;
            var res = TMPTextContainerCleaner.CleanAll();
            _tmpMetric.text = "0";

            RenderDiffCard(
                _tmpResultPanel,
                title: "✓ TextContainers Obsoletos Removidos",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: 0,
                benefitDesc: $"Removidos {res.ObjectsFixed} TextContainer(s) obsoletos ({res.ScenesChanged} cena(s), {res.PrefabsChanged} prefab(s)).",
                modifications: res.Modifications);

            SetStatus($"Corrigidos {res.ObjectsFixed} TextContainer(s) obsoletos.");
        }

        private void RunAnimFix(AnimatorTransitionCleaner.FixMode mode)
        {
            var before = _lastAnimCount;
            var res = AnimatorTransitionCleaner.CleanAll(mode);

            RenderDiffCard(
                _animResultPanel,
                title: mode == AnimatorTransitionCleaner.FixMode.EnableExitTime ? "✓ Exit Time Habilitado nas Transições" : "✓ Transições Inválidas Removidas",
                beforeCount: before > 0 ? before : res.TransitionsFixed,
                afterCount: 0,
                benefitDesc: $"Corrigidas {res.TransitionsFixed} transição(ões) em {res.ControllersModified} Animator Controller(s). Warnings de 'doesn't have an Exit Time' suprimidos.",
                modifications: res.Modifications);

            SetStatus($"Corrigidas {res.TransitionsFixed} transição(ões) de Animator.");
        }

        private void RunLegacyResavePlugins()
        {
            var before = _lastLegacyCount;
            var res = LegacyComponentCleaner.ResaveLegacyPluginImportersWithResult();

            RenderDiffCard(
                _legacyResultPanel,
                title: "✓ Metadados de Plugins Atualizados para Versão 2",
                beforeCount: before > 0 ? before : res.PluginsUpdated,
                afterCount: 0,
                benefitDesc: $"Re-salvos e atualizados {res.PluginsUpdated} arquivo(s) .meta de plugins legados. Aviso 'contains a PluginImporter object at version 1' no Unity 6 completamente eliminado.",
                modifications: res.Modifications);

            SetStatus($"Re-salvos {res.PluginsUpdated} importador(es) de plugins legados.");
        }

        private void RunLegacyResaveAll()
        {
            LegacyComponentCleaner.ResaveAllScenesAndPrefabs();

            RenderDiffCard(
                _legacyResultPanel,
                title: "✓ Projeto Totalmente Modernizado e Reserializado",
                beforeCount: _lastLegacyCount,
                afterCount: 0,
                benefitDesc: "Todas as cenas, prefabs e ScriptableObjects foram reserializados e atualizados para o formato nativo do Unity atual. GUILayers e referências órfãs purgadas.",
                modifications: null);

            SetStatus("Todas as cenas, prefabs e metas de plugins foram resalvos e modernizados.");
        }

#if WAGENHEIMER_UNITYUTILS_IAP
        private void RunIapFixAll()
        {
            var before = _lastIapCount;
            var res = UnusedIAPButtonCleaner.CleanAll();

            RenderDiffCard(
                _iapResultPanel,
                title: "✓ Botões Codeless IAP Limpos",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: 0,
                benefitDesc: $"Removidos {res.ObjectsFixed} botão(ões) IAP inativos sem eventos vinculados ({res.ScenesChanged} cena(s), {res.PrefabsChanged} prefab(s)).",
                modifications: res.Modifications);

            SetStatus($"Removidos {res.ObjectsFixed} componente(s) IAPButton inativos.");
        }
#endif

        #endregion

        #region UI Rendering Helpers

        private void RenderScanResults(
            VisualElement panel,
            int issueCount,
            string successMsg,
            string diagTitle,
            string diagDesc,
            string recDesc,
            string fixBtnText,
            Action fixAction,
            List<CleanupIssueItem> items)
        {
            panel.Clear();
            panel.style.display = DisplayStyle.Flex;

            if (issueCount == 0)
            {
                var successBox = new VisualElement();
                successBox.AddToClassList("clean-success-banner");

                var successLabel = new Label($"✓ {successMsg}");
                successLabel.AddToClassList("clean-success-text");
                successBox.Add(successLabel);

                panel.Add(successBox);
                return;
            }

            // Solution Banner
            var solutionBox = new VisualElement();
            solutionBox.AddToClassList("solution-banner");

            var solTitle = new Label($"💡 {diagTitle} ({issueCount} item(s) encontrado(s))");
            solTitle.AddToClassList("solution-title");
            solutionBox.Add(solTitle);

            var solDesc = new Label($"{diagDesc}\n\n{recDesc}");
            solDesc.AddToClassList("solution-desc");
            solutionBox.Add(solDesc);

            if (fixAction != null && !string.IsNullOrEmpty(fixBtnText))
            {
                var actionRow = new VisualElement();
                actionRow.AddToClassList("solution-action-row");

                var fixBtn = UnityUtilsUIStyle.CreateButton(fixBtnText, "btn-primary", () => RunGuarded("Recommended fix", fixAction));
                fixBtn.style.alignSelf = Align.FlexStart;
                actionRow.Add(fixBtn);

                solutionBox.Add(actionRow);
            }

            panel.Add(solutionBox);

            // Detailed items list
            if (items != null && items.Count > 0)
            {
                var listContainer = new ScrollView();
                listContainer.AddToClassList("items-list-container");

                foreach (var item in items)
                {
                    var row = new VisualElement();
                    row.AddToClassList("item-row");

                    var content = new VisualElement();
                    content.AddToClassList("item-content");

                    var pathLabel = new Label($"{(item.IsScene ? "🎬 [Cena] " : item.IsPrefab ? "📦 [Prefab] " : "📄 ")}{item.AssetPath}");
                    pathLabel.AddToClassList("item-path");

                    var descLabel = new Label(item.Description);
                    descLabel.AddToClassList("item-desc");

                    content.Add(pathLabel);
                    content.Add(descLabel);
                    row.Add(content);

                    var pingBtn = new Button(() => CleanupAssetUtility.PingAsset(item.AssetPath));
                    pingBtn.text = "Inspecionar";
                    pingBtn.AddToClassList("btn-ping");
                    row.Add(pingBtn);

                    listContainer.Add(row);
                }

                panel.Add(listContainer);
            }
        }

        private void RenderDiffCard(
            VisualElement panel,
            string title,
            int beforeCount,
            int afterCount,
            string benefitDesc,
            List<CleanupModificationRecord> modifications)
        {
            panel.Clear();
            panel.style.display = DisplayStyle.Flex;

            var diffCard = new VisualElement();
            diffCard.AddToClassList("diff-card");

            var header = new VisualElement();
            header.AddToClassList("diff-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("diff-title");

            var badge = new Label(beforeCount > 0 ? $"{beforeCount} ➔ {afterCount} resolvido" : "✓ 100% Otimizado");
            badge.AddToClassList("diff-stat-badge");

            header.Add(titleLabel);
            header.Add(badge);
            diffCard.Add(header);

            var desc = new Label(benefitDesc);
            desc.AddToClassList("diff-desc");
            diffCard.Add(desc);

            if (modifications != null && modifications.Count > 0)
            {
                var list = new ScrollView();
                list.AddToClassList("diff-list");

                foreach (var mod in modifications)
                {
                    var item = new Label($"• {mod.AssetPath} ➔ {mod.ActionTaken}");
                    item.AddToClassList("diff-item");
                    list.Add(item);
                }

                diffCard.Add(list);
            }

            panel.Add(diffCard);
        }

        #endregion

        private Button CreateActionButton(string label, string styleClass, string operation, Action action)
        {
            return UnityUtilsUIStyle.CreateButton(label, styleClass, () => RunGuarded(operation, action));
        }

        private void RunGuarded(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus($"{operation} failed: {ex.Message}");
            }
        }

        private void SetStatus(string message)
        {
            if (_statusLabel == null)
            {
                Debug.Log($"[CleanupSuite] {message}");
                return;
            }

            _statusLabel.text = message;
            Debug.Log($"[CleanupSuite] {message}");
        }
    }
}
