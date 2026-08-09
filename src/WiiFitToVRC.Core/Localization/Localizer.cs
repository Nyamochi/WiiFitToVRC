using System.Globalization;
using WiiFitToVRC.Core.Settings;

namespace WiiFitToVRC.Core.Localization;

/// <summary>
/// Minimal string-table localization. AppLanguage.Auto resolves once from
/// CultureInfo.CurrentUICulture (the Windows display language) at lookup time; anything not
/// covered by the resolver falls back to English.
/// </summary>
public static class Localizer
{
    // key -> [en, ja, zh-Hans, zh-Hant, ko, fr, de, it]
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["Status_NotConnected"] = ["Not connected", "未接続", "未连接", "未連接", "연결 안 됨", "Non connecté", "Nicht verbunden", "Non connesso"],
        ["Button_ConnectPrompt"] = ["Press the SYNC button, then connect", "SYNCボタンを押してから接続", "按下SYNC按钮后连接", "按下SYNC按鈕後連接", "SYNC 버튼을 누른 후 연결", "Appuyez sur SYNC puis connectez", "SYNC-Taste drücken, dann verbinden", "Premi SYNC, poi connetti"],
        ["Button_ConnectAbort"] = ["Cancel", "接続中断", "中断连接", "中斷連接", "연결 중단", "Annuler", "Abbrechen", "Annulla"],
        ["Button_Connected"] = ["Connected", "接続済み", "已连接", "已連接", "연결됨", "Connecté", "Verbunden", "Connesso"],
        ["Button_Disconnect"] = ["Disconnect", "切断", "断开连接", "斷開連接", "연결 끊기", "Déconnecter", "Trennen", "Disconnetti"],
        ["Button_Calibrate"] = ["Calibrate", "キャリブレーション", "校准", "校準", "보정", "Étalonnage", "Kalibrierung", "Calibrazione"],
        ["Caption_PressureDistribution"] = ["Pressure distribution", "荷重分布", "压力分布", "壓力分佈", "압력 분포", "Répartition de pression", "Druckverteilung", "Distribuzione pressione"],
        ["Caption_DetectedDirection"] = ["Detected direction", "検知方向", "检测方向", "偵測方向", "감지 방향", "Direction détectée", "Erkannte Richtung", "Direzione rilevata"],
        ["Caption_RawValues"] = ["Raw values", "軸の値(生値)", "原始数值", "原始數值", "원시 값", "Valeurs brutes", "Rohwerte", "Valori grezzi"],
        ["Caption_CalibratedValues"] = ["Calibrated (%)", "キャリブレーション後(%)", "校准后(%)", "校準後(%)", "보정 후(%)", "Étalonné (%)", "Kalibriert (%)", "Calibrato (%)"],
        ["Button_RecordStart"] = ["Start recording", "記録開始", "开始记录", "開始記錄", "기록 시작", "Démarrer l'enregistrement", "Aufnahme starten", "Avvia registrazione"],
        ["Button_RecordStop"] = ["Stop recording", "記録停止", "停止记录", "停止記錄", "기록 중지", "Arrêter l'enregistrement", "Aufnahme stoppen", "Interrompi registrazione"],
        ["Status_Pairing"] = ["Pairing (press the SYNC button)...", "ペアリング中(SYNCボタンを押してください)...", "配对中(请按下SYNC按钮)...", "配對中(請按下SYNC按鈕)...", "페어링 중(SYNC 버튼을 눌러주세요)...", "Appairage en cours (appuyez sur SYNC)...", "Kopplung läuft (SYNC-Taste drücken)...", "Associazione in corso (premi SYNC)..."],
        ["Status_HidConnecting"] = ["Connecting to device...", "HID接続試行中...", "正在连接设备...", "正在連接裝置...", "장치에 연결 중...", "Connexion à l'appareil...", "Verbindung zum Gerät wird hergestellt...", "Connessione al dispositivo..."],
        ["Status_HidTimeout"] = ["Connection timed out. The board may have disconnected again after pairing. Please press SYNC and try again.", "HID接続がタイムアウトしました。ペアリング後にボードが再び切断した可能性があります。もう一度SYNCボタンを押してからお試しください。", "连接超时。配对后设备可能再次断开。请再次按下SYNC按钮重试。", "連接逾時。配對後裝置可能再次斷開。請再次按下SYNC按鈕重試。", "연결이 시간 초과되었습니다. 페어링 후 보드 연결이 다시 끊어졌을 수 있습니다. SYNC 버튼을 다시 눌러 시도해 주세요.", "Délai de connexion dépassé. La planche s'est peut-être déconnectée après l'appairage. Appuyez à nouveau sur SYNC.", "Zeitüberschreitung bei der Verbindung. Das Board hat sich möglicherweise nach der Kopplung erneut getrennt. Bitte SYNC erneut drücken.", "Timeout della connessione. La board potrebbe essersi disconnessa di nuovo dopo l'associazione. Premi di nuovo SYNC e riprova."],
        ["Status_Aborting"] = ["Cancelling...", "中断中...", "正在中断...", "正在中斷...", "중단 중...", "Annulation...", "Wird abgebrochen...", "Annullamento..."],
        ["Status_Aborted"] = ["Connection cancelled.", "接続を中断しました。", "已中断连接。", "已中斷連接。", "연결이 중단되었습니다.", "Connexion annulée.", "Verbindung abgebrochen.", "Connessione annullata."],
        ["Status_Error"] = ["Error: {0}", "エラー: {0}", "错误: {0}", "錯誤: {0}", "오류: {0}", "Erreur : {0}", "Fehler: {0}", "Errore: {0}"],
        ["Status_PairFail"] = ["Failed: {0} {1}", "失敗: {0} {1}", "失败: {0} {1}", "失敗: {0} {1}", "실패: {0} {1}", "Échec : {0} {1}", "Fehlgeschlagen: {0} {1}", "Fallito: {0} {1}"],
        ["Status_Disconnected"] = ["Disconnected ({0}). Please press SYNC to reconnect.", "切断されました({0})。再度SYNCボタンを押して接続してください。", "已断开连接({0})。请再次按下SYNC按钮重新连接。", "已斷開連接({0})。請再次按下SYNC按鈕重新連接。", "연결이 끊어졌습니다({0}). SYNC 버튼을 다시 눌러 재연결하세요.", "Déconnecté ({0}). Appuyez sur SYNC pour vous reconnecter.", "Verbindung getrennt ({0}). Bitte SYNC drücken, um erneut zu verbinden.", "Disconnesso ({0}). Premi SYNC per riconnetterti."],
        ["Status_CalibratingPrompt"] = ["Calibrating (10 seconds - please step off the board and leave it empty)...", "キャリブレーション中(10秒間、ボードから降りて何も乗せない状態にしてください)...", "校准中(请在10秒内离开踏板,保持踏板空载)...", "校準中(請在10秒內離開踏板,保持踏板空載)...", "보정 중(10초 동안 보드에서 내려와 아무것도 올려두지 마세요)...", "Étalonnage (10 secondes - descendez de la planche, laissez-la vide)...", "Kalibrierung (10 Sekunden - bitte vom Board steigen und es leer lassen)...", "Calibrazione (10 secondi - scendi dalla pedana e lasciala vuota)..."],
        ["Status_CalibrationDone"] = ["Connected (calibration complete - you can step back on)", "接続済み(キャリブレーション完了。ボードに乗ってください)", "已连接(校准完成,可以踩上踏板)", "已連接(校準完成,可以踩上踏板)", "연결됨(보정 완료. 보드에 올라가세요)", "Connecté (étalonnage terminé - vous pouvez remonter)", "Verbunden (Kalibrierung abgeschlossen - du kannst wieder aufsteigen)", "Connesso (calibrazione completata - puoi risalire)"],
        ["Status_WeightCalibrating"] = ["Weight calibrating (please stand normally for a while)...", "体重キャリブレーション中(しばらく普通に立っていてください)...", "体重校准中(请正常站立一会儿)...", "體重校準中(請正常站立一會兒)...", "체중 보정 중(잠시 평소처럼 서 있어 주세요)...", "Étalonnage du poids (tenez-vous normalement un moment)...", "Gewichtskalibrierung läuft (bitte kurz normal stehen bleiben)...", "Calibrazione del peso in corso (resta in piedi normalmente per un momento)..."],
        ["Status_WeightCalibrationRefreshed"] = ["Weight calibration performed", "体重キャリブレーションを実施しました", "已执行体重校准", "已執行體重校準", "체중 보정을 실시했습니다", "Étalonnage du poids effectué", "Gewichtskalibrierung durchgeführt", "Calibrazione del peso eseguita"],
        ["Record_Recording"] = ["Recording: {0} -> {1}", "記録中: {0} -> {1}", "记录中: {0} -> {1}", "記錄中: {0} -> {1}", "기록 중: {0} -> {1}", "Enregistrement : {0} -> {1}", "Aufnahme: {0} -> {1}", "Registrazione: {0} -> {1}"],
        ["Record_Stopped"] = ["Recording stopped", "記録停止しました", "记录已停止", "記錄已停止", "기록이 중지되었습니다", "Enregistrement arrêté", "Aufnahme gestoppt", "Registrazione interrotta"],
        ["Button_Settings"] = ["Settings", "設定", "设置", "設定", "설정", "Paramètres", "Einstellungen", "Impostazioni"],
        ["Settings_Language"] = ["Language", "言語 (Language)", "语言 (Language)", "語言 (Language)", "언어 (Language)", "Langue (Language)", "Sprache (Language)", "Lingua (Language)"],
        ["Settings_LanguageAuto"] = ["Auto (follow Windows setting)", "自動(OS設定に従う)", "自动(跟随系统设置)", "自動(跟隨系統設定)", "자동(Windows 설정 따름)", "Automatique (suivre Windows)", "Automatisch (Windows-Einstellung folgen)", "Automatico (segui Windows)"],
        ["Settings_OutputMode"] = ["Output mode", "出力方式", "输出方式", "輸出方式", "출력 방식", "Mode de sortie", "Ausgabemodus", "Modalità di uscita"],
        ["Settings_OutputMode_Keyboard"] = ["Keyboard (turn via Q/E)", "キーボード(Q/Eで旋回)", "键盘(Q/E转向)", "鍵盤(Q/E轉向)", "키보드(Q/E로 회전)", "Clavier (rotation Q/E)", "Tastatur (Drehung mit Q/E)", "Tastiera (rotazione Q/E)"],
        ["Settings_OutputMode_KeyboardMouse"] = ["Keyboard + mouse (turn via camera look)", "キーボード+マウス(視点移動で旋回)", "键盘+鼠标(视角转向)", "鍵盤+滑鼠(視角轉向)", "키보드+마우스(시점 이동으로 회전)", "Clavier + souris (rotation à la vue caméra)", "Tastatur + Maus (Drehung per Kamerablick)", "Tastiera + mouse (rotazione con la visuale)"],
        ["Settings_OutputMode_Controller"] = ["Virtual controller (for VRChat)", "仮想コントローラー(VRChat向け)", "虚拟手柄(适用于VRChat)", "虛擬手把(適用於VRChat)", "가상 컨트롤러(VRChat용)", "Manette virtuelle (pour VRChat)", "Virtueller Controller (für VRChat)", "Controller virtuale (per VRChat)"],
        ["Settings_OutputMode_Osc"] = ["VRChat OSC (for locked-input VR headsets)", "OSC機能(VRC専用)", "使用VRChat的OSC功能(适用于VR设备锁定输入的环境)", "使用VRChat的OSC功能(適用於VR裝置鎖定輸入的環境)", "VRChat OSC 사용(VR 기기에 입력이 잠긴 환경용)", "OSC de VRChat (pour casques VR à entrée verrouillée)", "VRChat-OSC (für VR-Headsets mit gesperrter Eingabe)", "OSC di VRChat (per visori VR con input bloccato)"],
        ["Settings_Tab_Controller"] = ["Controller", "コントローラー", "手柄", "手把", "컨트롤러", "Manette", "Controller", "Controller"],
        ["Settings_ControllerStatus_OK"] = ["Virtual controller connected", "仮想コントローラー接続済み", "虚拟手柄已连接", "虛擬手把已連接", "가상 컨트롤러 연결됨", "Manette virtuelle connectée", "Virtueller Controller verbunden", "Controller virtuale connesso"],
        ["Settings_ControllerStatus_Unavailable"] = ["Unavailable: {0} (install ViGEmBus)", "利用不可: {0} (ViGEmBusをインストールしてください)", "不可用: {0}(请安装 ViGEmBus)", "不可用: {0}(請安裝 ViGEmBus)", "사용 불가: {0} (ViGEmBus 설치 필요)", "Indisponible : {0} (installez ViGEmBus)", "Nicht verfügbar: {0} (ViGEmBus installieren)", "Non disponibile: {0} (installa ViGEmBus)"],
        ["Settings_ControllerStatus_NotConnectedYet"] = ["Not connected yet (select controller mode and connect the board)", "まだ未接続(コントローラーモードを選んでボードに接続してください)", "尚未连接(请选择手柄模式并连接踏板)", "尚未連接(請選擇手把模式並連接踏板)", "아직 연결되지 않음(컨트롤러 모드를 선택하고 보드에 연결하세요)", "Pas encore connecté (sélectionnez le mode manette et connectez la planche)", "Noch nicht verbunden (Controller-Modus wählen und Board verbinden)", "Non ancora connesso (seleziona la modalità controller e connetti la pedana)"],
        ["Settings_ControllerStrokeRight"] = ["Turn-right stick deflection (%)", "右回転のスティック量(%)", "右转摇杆偏移(%)", "右轉搖桿偏移(%)", "우회전 스틱 편향(%)", "Déviation du stick (droite, %)", "Stick-Auslenkung (rechts, %)", "Deflessione stick (destra, %)"],
        ["Settings_ControllerStrokeLeft"] = ["Turn-left stick deflection (%)", "左回転のスティック量(%)", "左转摇杆偏移(%)", "左轉搖桿偏移(%)", "좌회전 스틱 편향(%)", "Déviation du stick (gauche, %)", "Stick-Auslenkung (links, %)", "Deflessione stick (sinistra, %)"],
        ["Settings_ControllerButton_Jump"] = ["Jump button", "ジャンプボタン", "跳跃按钮", "跳躍按鈕", "점프 버튼", "Bouton de saut", "Sprungtaste", "Pulsante salto"],
        ["Settings_ControllerButton_Crouch"] = ["Crouch button", "しゃがみボタン", "蹲下按钮", "蹲下按鈕", "웅크리기 버튼", "Bouton accroupi", "Duck-Taste", "Pulsante accovacciati"],
        ["Settings_ControllerButton_Dash"] = ["Dash (sprint) button", "ダッシュボタン", "冲刺按钮", "衝刺按鈕", "대시 버튼", "Bouton de sprint", "Sprint-Taste", "Pulsante scatto"],
        ["Settings_MouseStrokeRight"] = ["Turn-right mouse sensitivity", "右回転のマウス感度", "右转鼠标灵敏度", "右轉滑鼠靈敏度", "우회전 마우스 감도", "Sensibilité souris (droite)", "Maus-Empfindlichkeit (rechts)", "Sensibilità mouse (destra)"],
        ["Settings_MouseStrokeLeft"] = ["Turn-left mouse sensitivity", "左回転のマウス感度", "左转鼠标灵敏度", "左轉滑鼠靈敏度", "좌회전 마우스 감도", "Sensibilité souris (gauche)", "Maus-Empfindlichkeit (links)", "Sensibilità mouse (sinistra)"],
        ["Settings_PresenceThreshold"] = ["Presence weight threshold", "反応する荷重のしきい値", "触发所需的重量阈值", "觸發所需的重量閾值", "반응 임계 하중", "Seuil de poids de présence", "Anwesenheits-Gewichtsschwelle", "Soglia di peso di presenza"],
        ["Settings_SleepSeconds"] = ["Seconds until sleep/wake", "スリープ・復帰までの秒数", "休眠/唤醒所需秒数", "休眠/喚醒所需秒數", "잠자기/깨우기까지 초", "Secondes avant veille/réveil", "Sekunden bis Ruhe/Aufwachen", "Secondi prima di sospensione/risveglio"],
        ["Settings_FootstepThreshold"] = ["Footstep threshold (% of reference weight)", "足踏み検知のしきい値(基準体重比%)", "踏步阈值(基准体重的%)", "踏步閾值(基準體重的%)", "발걸음 감지 임계값(기준 체중 대비 %)", "Seuil de pas (% du poids de référence)", "Schwellwert Fußtritt (% des Referenzgewichts)", "Soglia passo (% del peso di riferimento)"],
        ["Settings_DashPeriod"] = ["Dash detection (ms between steps)", "ダッシュ判定(歩幅の間隔ms)", "冲刺判定(步伐间隔ms)", "衝刺判定(步伐間隔ms)", "대시 판정(걸음 간격 ms)", "Détection de sprint (ms entre les pas)", "Sprint-Erkennung (ms zwischen Schritten)", "Rilevamento scatto (ms tra i passi)"],
        ["Settings_StepHold"] = ["Stride length (ms)", "歩幅(ms)", "步幅(ms)", "步幅(ms)", "보폭(ms)", "Longueur de foulée (ms)", "Schrittlänge (ms)", "Lunghezza del passo (ms)"],
        ["Settings_CrouchEnabled"] = ["Crouch detection enabled", "しゃがみ検知を有効にする", "启用蹲下检测", "啟用蹲下偵測", "웅크리기 감지 사용", "Détection de l'accroupissement activée", "Ducken-Erkennung aktiviert", "Rilevamento accovacciamento attivo"],
        ["Settings_JumpEnabled"] = ["Jump detection enabled", "ジャンプ検知を有効にする", "启用跳跃检测", "啟用跳躍偵測", "점프 감지 사용", "Détection du saut activée", "Sprung-Erkennung aktiviert", "Rilevamento salto attivo"],
        ["Settings_TurnEnabled"] = ["Turning enabled", "旋回動作を有効にする", "启用转向动作", "啟用轉向動作", "회전 동작 사용", "Rotation activée", "Drehen aktiviert", "Rotazione attiva"],
        ["Settings_DebugMode"] = ["Debug mode (show recording controls)", "デバッグモード(記録操作を表示)", "调试模式(显示记录控件)", "偵錯模式(顯示記錄控制項)", "디버그 모드(기록 컨트롤 표시)", "Mode débogage (afficher les contrôles d'enregistrement)", "Debug-Modus (Aufnahmesteuerung anzeigen)", "Modalità debug (mostra controlli di registrazione)"],
        ["Settings_DebugFolder"] = ["Debug output folder", "デバッグ出力先", "调试输出文件夹", "偵錯輸出資料夾", "디버그 출력 폴더", "Dossier de sortie debug", "Debug-Ausgabeordner", "Cartella di output debug"],
        ["Settings_Tab_General"] = ["General", "全般", "常规", "一般", "일반", "Général", "Allgemein", "Generale"],
        ["Settings_Tab_Keybinds"] = ["Keybinds", "キー割り当て", "按键绑定", "按鍵綁定", "키 바인딩", "Touches", "Tastenbelegung", "Tasti"],
        ["Settings_Key_Forward"] = ["Forward", "前進", "前进", "前進", "전진", "Avancer", "Vorwärts", "Avanti"],
        ["Settings_Key_Dash"] = ["Dash (main key)", "ダッシュ(主キー)", "冲刺(主键)", "衝刺(主鍵)", "대시(주 키)", "Sprint (touche principale)", "Sprint (Haupttaste)", "Scatto (tasto principale)"],
        ["Settings_Key_DashModifier"] = ["Dash (modifier key)", "ダッシュ(修飾キー)", "冲刺(修饰键)", "衝刺(修飾鍵)", "대시(보조 키)", "Sprint (touche modificatrice)", "Sprint (Zusatztaste)", "Scatto (tasto modificatore)"],
        ["Settings_Key_Backward"] = ["Backward", "後進", "后退", "後退", "후진", "Reculer", "Rückwärts", "Indietro"],
        ["Settings_Key_TurnRight"] = ["Turn right (key mode)", "右回転(キーモード)", "右转(按键模式)", "右轉(按鍵模式)", "우회전(키 모드)", "Tourner à droite (touche)", "Rechts drehen (Taste)", "Gira a destra (tasto)"],
        ["Settings_Key_TurnLeft"] = ["Turn left (key mode)", "左回転(キーモード)", "左转(按键模式)", "左轉(按鍵模式)", "좌회전(키 모드)", "Tourner à gauche (touche)", "Links drehen (Taste)", "Gira a sinistra (tasto)"],
        ["Settings_Key_Jump"] = ["Jump", "ジャンプ", "跳跃", "跳躍", "점프", "Sauter", "Springen", "Salta"],
        ["Settings_Key_Crouch"] = ["Crouch", "しゃがみ", "蹲下", "蹲下", "웅크리기", "S'accroupir", "Ducken", "Accovacciati"],
        ["Settings_Save"] = ["Save", "保存", "保存", "儲存", "저장", "Enregistrer", "Speichern", "Salva"],
        ["Settings_Cancel"] = ["Cancel", "キャンセル", "取消", "取消", "취소", "Annuler", "Abbrechen", "Annulla"],
    };

    private static readonly AppLanguage[] Order =
    [
        AppLanguage.English, AppLanguage.Japanese, AppLanguage.ChineseSimplified, AppLanguage.ChineseTraditional,
        AppLanguage.Korean, AppLanguage.French, AppLanguage.German, AppLanguage.Italian,
    ];

    /// <summary>Each language's own name, always shown in itself (a language picker convention) --
    /// with the English name in parentheses for every non-English entry, so someone who can't read
    /// the current UI language can still recognize their own in the list.</summary>
    public static readonly (AppLanguage Language, string NativeName)[] SelectableLanguages =
    [
        (AppLanguage.Auto, "__AUTO__"), // caller substitutes the localized Settings_LanguageAuto string
        (AppLanguage.English, "English"),
        (AppLanguage.Japanese, "日本語 (Japanese)"),
        (AppLanguage.ChineseSimplified, "简体中文 (Chinese Simplified)"),
        (AppLanguage.ChineseTraditional, "繁體中文 (Chinese Traditional)"),
        (AppLanguage.Korean, "한국어 (Korean)"),
        (AppLanguage.French, "Français (French)"),
        (AppLanguage.German, "Deutsch (German)"),
        (AppLanguage.Italian, "Italiano (Italian)"),
    ];

    public static AppLanguage ResolveAuto()
    {
        string name = CultureInfo.CurrentUICulture.Name; // e.g. "ja-JP", "zh-Hant-TW", "en-US"
        string twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (twoLetter == "ja") return AppLanguage.Japanese;
        if (twoLetter == "ko") return AppLanguage.Korean;
        if (twoLetter == "fr") return AppLanguage.French;
        if (twoLetter == "de") return AppLanguage.German;
        if (twoLetter == "it") return AppLanguage.Italian;
        if (twoLetter == "zh")
        {
            return name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.ChineseTraditional
                : AppLanguage.ChineseSimplified;
        }
        return AppLanguage.English;
    }

    public static string Get(string key, AppLanguage language)
    {
        if (language == AppLanguage.Auto)
        {
            language = ResolveAuto();
        }

        if (!Table.TryGetValue(key, out string[]? values))
        {
            return key;
        }

        int index = Array.IndexOf(Order, language);
        if (index < 0 || index >= values.Length)
        {
            index = 0; // English
        }
        return values[index];
    }

    public static string GetFormatted(string key, AppLanguage language, params object[] args) =>
        string.Format(Get(key, language), args);
}
