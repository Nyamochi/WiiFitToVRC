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
        ["Button_ConnectPrompt"] = ["Search again", "再検索", "重新搜索", "重新搜尋", "다시 검색", "Rechercher à nouveau", "Erneut suchen", "Cerca di nuovo"],
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
        ["Status_Pairing"] = ["Automatically searching (press the SYNC button)...", "自動で検索中です(SYNCボタンを押してください)...", "正在自动搜索(请按下SYNC按钮)...", "正在自動搜尋(請按下SYNC按鈕)...", "자동으로 검색 중입니다(SYNC 버튼을 눌러주세요)...", "Recherche automatique en cours (appuyez sur SYNC)...", "Automatische Suche läuft (SYNC-Taste drücken)...", "Ricerca automatica in corso (premi SYNC)..."],
        ["Status_HidConnecting"] = ["Connecting to device...", "Bluetooth接続待機中(HID)...", "正在连接设备...", "正在連接裝置...", "장치에 연결 중...", "Connexion à l'appareil...", "Verbindung zum Gerät wird hergestellt...", "Connessione al dispositivo..."],
        ["Status_HidTimeout"] = ["Connection timed out. The board may have disconnected again after pairing. Please press SYNC and try again.", "HID接続がタイムアウトしました。ペアリング後にボードが再び切断した可能性があります。もう一度SYNCボタンを押してからお試しください。", "连接超时。配对后设备可能再次断开。请再次按下SYNC按钮重试。", "連接逾時。配對後裝置可能再次斷開。請再次按下SYNC按鈕重試。", "연결이 시간 초과되었습니다. 페어링 후 보드 연결이 다시 끊어졌을 수 있습니다. SYNC 버튼을 다시 눌러 시도해 주세요.", "Délai de connexion dépassé. La planche s'est peut-être déconnectée après l'appairage. Appuyez à nouveau sur SYNC.", "Zeitüberschreitung bei der Verbindung. Das Board hat sich möglicherweise nach der Kopplung erneut getrennt. Bitte SYNC erneut drücken.", "Timeout della connessione. La board potrebbe essersi disconnessa di nuovo dopo l'associazione. Premi di nuovo SYNC e riprova."],
        ["Status_Aborting"] = ["Cancelling...", "中断中...", "正在中断...", "正在中斷...", "중단 중...", "Annulation...", "Wird abgebrochen...", "Annullamento..."],
        ["Status_Aborted"] = ["Connection cancelled.", "接続を中断しました。", "已中断连接。", "已中斷連接。", "연결이 중단되었습니다.", "Connexion annulée.", "Verbindung abgebrochen.", "Connessione annullata."],
        ["Status_Error"] = ["Error: {0}", "エラー: {0}", "错误: {0}", "錯誤: {0}", "오류: {0}", "Erreur : {0}", "Fehler: {0}", "Errore: {0}"],
        ["Status_PairFail"] = ["Failed: {0} {1}", "失敗: {0} {1}", "失败: {0} {1}", "失敗: {0} {1}", "실패: {0} {1}", "Échec : {0} {1}", "Fehlgeschlagen: {0} {1}", "Fallito: {0} {1}"],
        ["Status_Disconnected"] = ["Disconnected ({0}). Please press SYNC to reconnect.", "切断されました({0})。再度SYNCボタンを押して接続してください。", "已断开连接({0})。请再次按下SYNC按钮重新连接。", "已斷開連接({0})。請再次按下SYNC按鈕重新連接。", "연결이 끊어졌습니다({0}). SYNC 버튼을 다시 눌러 재연결하세요.", "Déconnecté ({0}). Appuyez sur SYNC pour vous reconnecter.", "Verbindung getrennt ({0}). Bitte SYNC drücken, um erneut zu verbinden.", "Disconnesso ({0}). Premi SYNC per riconnetterti."],
        ["Status_CalibrationAutoPrompt"] = ["Calibration is about to start. Please place the board on the floor (starting automatically in 5 seconds)...", "キャリブレーションを開始します。機器を床に置いてください(5秒後に自動開始)...", "即将开始校准。请将设备放在地板上(5秒后自动开始)...", "即將開始校準。請將裝置放在地板上(5秒後自動開始)...", "곧 보정을 시작합니다. 기기를 바닥에 놓아주세요(5초 후 자동 시작)...", "L'étalonnage va commencer. Posez l'appareil au sol (démarrage automatique dans 5 secondes)...", "Die Kalibrierung beginnt gleich. Bitte das Gerät auf den Boden stellen (startet automatisch in 5 Sekunden)...", "La calibrazione sta per iniziare. Posiziona il dispositivo sul pavimento (avvio automatico tra 5 secondi)..."],
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
        ["Settings_OutputMode_Controller"] = ["Virtual controller", "仮想コントローラー", "虚拟手柄", "虛擬手把", "가상 컨트롤러", "Manette virtuelle", "Virtueller Controller", "Controller virtuale"],
        ["Settings_OutputMode_Osc"] = ["VRChat OSC (for locked-input VR headsets)", "OSC機能(VRC専用)", "使用VRChat的OSC功能(适用于VR设备锁定输入的环境)", "使用VRChat的OSC功能(適用於VR裝置鎖定輸入的環境)", "VRChat OSC 사용(VR 기기에 입력이 잠긴 환경용)", "OSC de VRChat (pour casques VR à entrée verrouillée)", "VRChat-OSC (für VR-Headsets mit gesperrter Eingabe)", "OSC di VRChat (per visori VR con input bloccato)"],
        ["Settings_Tab_Controller"] = ["Controller", "コントローラー", "手柄", "手把", "컨트롤러", "Manette", "Controller", "Controller"],
        ["Settings_ControllerStatus_OK"] = ["Virtual controller connected", "仮想コントローラー接続済み", "虚拟手柄已连接", "虛擬手把已連接", "가상 컨트롤러 연결됨", "Manette virtuelle connectée", "Virtueller Controller verbunden", "Controller virtuale connesso"],
        ["Settings_ControllerStatus_Unavailable"] = ["Unavailable: {0} (install ViGEmBus)", "利用不可: {0} (ViGEmBusをインストールしてください)", "不可用: {0}(请安装 ViGEmBus)", "不可用: {0}(請安裝 ViGEmBus)", "사용 불가: {0} (ViGEmBus 설치 필요)", "Indisponible : {0} (installez ViGEmBus)", "Nicht verfügbar: {0} (ViGEmBus installieren)", "Non disponibile: {0} (installa ViGEmBus)"],
        ["Settings_ControllerStatus_NotConnectedYet"] = ["Not connected yet (select controller mode and connect the board)", "まだ未接続(コントローラーモードを選んでボードに接続してください)", "尚未连接(请选择手柄模式并连接踏板)", "尚未連接(請選擇手把模式並連接踏板)", "아직 연결되지 않음(컨트롤러 모드를 선택하고 보드에 연결하세요)", "Pas encore connecté (sélectionnez le mode manette et connectez la planche)", "Noch nicht verbunden (Controller-Modus wählen und Board verbinden)", "Non ancora connesso (seleziona la modalità controller e connetti la pedana)"],
        ["Settings_ControllerButton_Jump"] = ["Jump button", "ジャンプボタン", "跳跃按钮", "跳躍按鈕", "점프 버튼", "Bouton de saut", "Sprungtaste", "Pulsante salto"],
        ["Settings_ControllerButton_Crouch"] = ["Crouch button", "しゃがみボタン", "蹲下按钮", "蹲下按鈕", "웅크리기 버튼", "Bouton accroupi", "Duck-Taste", "Pulsante accovacciati"],
        ["Settings_ControllerButton_Dash"] = ["Dash (sprint) button", "ダッシュボタン", "冲刺按钮", "衝刺按鈕", "대시 버튼", "Bouton de sprint", "Sprint-Taste", "Pulsante scatto"],
        ["Settings_DashInputMode"] = ["Dash input method", "ダッシュの入力方式", "冲刺输入方式", "衝刺輸入方式", "대시 입력 방식", "Méthode d'entrée du sprint", "Sprint-Eingabemethode", "Metodo di input dello scatto"],
        ["Settings_DashInputMode_ComboKey"] = ["Combo key", "複合キー", "组合键", "組合鍵", "조합 키", "Touche combinée", "Tastenkombination", "Tasto combinato"],
        ["Settings_DashInputMode_DoubleTap"] = ["Double-tap key", "キー2度押し", "按键连按两次", "按鍵連按兩次", "키 두 번 누르기", "Double-appui", "Doppeltippen", "Doppio tocco"],
        ["Settings_TurnSpeed"] = ["Turn speed", "旋回の速さ", "转向速度", "轉向速度", "회전 속도", "Vitesse de rotation", "Drehgeschwindigkeit", "Velocità di rotazione"],
        ["Settings_NoSetting"] = ["No setting", "設定なし", "无设置项", "無設定項", "설정 없음", "Aucun réglage", "Keine Einstellung", "Nessuna impostazione"],
        ["Settings_TurnHold"] = ["Turn length", "旋回の長さ", "转向长度", "轉向長度", "회전 길이", "Durée de rotation", "Drehdauer", "Durata di rotazione"],
        ["Settings_PresenceThreshold"] = ["Presence weight threshold", "反応する荷重のしきい値", "触发所需的重量阈值", "觸發所需的重量閾值", "반응 임계 하중", "Seuil de poids de présence", "Anwesenheits-Gewichtsschwelle", "Soglia di peso di presenza"],
        ["Settings_SleepSeconds"] = ["Seconds until sleep/wake", "スリープ・復帰までの秒数", "休眠/唤醒所需秒数", "休眠/喚醒所需秒數", "잠자기/깨우기까지 초", "Secondes avant veille/réveil", "Sekunden bis Ruhe/Aufwachen", "Secondi prima di sospensione/risveglio"],
        ["Settings_GestureSensitivity_Group"] = ["Gesture sensitivity", "反応のしやすさ", "反应灵敏度", "反應靈敏度", "반응 민감도", "Sensibilité des gestes", "Gesten-Empfindlichkeit", "Sensibilità dei gesti"],
        ["Settings_GestureSensitivity_Walk"] = ["Walk", "歩き", "行走", "行走", "걷기", "Marche", "Gehen", "Camminata"],
        ["Settings_GestureSensitivity_Dash"] = ["Dash", "ダッシュ", "冲刺", "衝刺", "대시", "Sprint", "Sprint", "Scatto"],
        ["Settings_GestureSensitivity_Turn"] = ["Turn", "旋回", "转向", "轉向", "회전", "Rotation", "Drehen", "Rotazione"],
        ["Settings_TurnMode_Hold"] = ["Hold", "長押し", "长按", "長按", "길게 유지", "Maintien", "Halten", "Mantieni"],
        ["Settings_TurnMode_Footstep"] = ["Footstep", "足踏み", "踏步", "踏步", "발걸음", "Pas alternés", "Schrittwechsel", "Passo"],
        ["Settings_GestureSensitivity_Jump"] = ["Jump", "ジャンプ", "跳跃", "跳躍", "점프", "Saut", "Sprung", "Salto"],
        ["Settings_GestureSensitivity_Crouch"] = ["Crouch", "しゃがみ", "下蹲", "蹲下", "웅크리기", "Accroupissement", "Ducken", "Accovacciamento"],
        ["Settings_GestureSensitivity_Stride"] = ["Stride", "歩幅", "步幅", "步幅", "보폭", "Longueur de foulée", "Schrittlänge", "Lunghezza del passo"],
        ["Settings_GestureSensitivity_StepContinuation"] = ["Walk/Dash continuation", "歩行/ダッシュの継続判定", "行走/冲刺持续判定", "行走/衝刺持續判定", "걷기/대시 지속 판정", "Continuité marche/sprint", "Lauf-/Sprint-Fortsetzung", "Continuità camminata/scatto"],
        ["Settings_GestureSensitivity_ContinuationStepCount"] = ["Steps until continuation", "継続判定までの歩数", "达到持续判定的步数", "達到持續判定的步數", "지속 판정까지의 걸음 수", "Pas avant continuation", "Schritte bis Fortsetzung", "Passi prima della continuità"],
        ["Settings_GestureSensitivity_Weak"] = ["Weak", "弱い", "弱", "弱", "약함", "Faible", "Schwach", "Debole"],
        ["Settings_GestureSensitivity_Strong"] = ["Strong", "強い", "强", "強", "강함", "Fort", "Stark", "Forte"],
        ["Settings_GestureSensitivity_Narrow"] = ["Narrow", "狭い", "窄", "窄", "좁음", "Étroit", "Eng", "Stretto"],
        ["Settings_GestureSensitivity_Wide"] = ["Wide", "広い", "宽", "寬", "넓음", "Large", "Weit", "Ampio"],
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
        ["Settings_ResetToDefaults"] = ["Defaults", "初期値", "默认值", "預設值", "기본값", "Par défaut", "Standard", "Predefiniti"],
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
