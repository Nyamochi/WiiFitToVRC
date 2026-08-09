# WiiFitToVRC

[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md) | [简体中文](README.zh-Hans.md) | **繁體中文**

這是一款將 Wii 平衡板變成 VRChat(或其他 Windows 應用程式)行走控制器的應用程式。只需站在平衡板上移動體重,就能將前進、後退、轉向、跳躍、蹲下轉換為鍵盤/滑鼠輸入、虛擬 Xbox 360 手把輸入,或 VRChat 自帶的 OSC 輸入。

## 簡單上手(非技術使用者指南)

完全不需要任何程式設計知識,只需以下幾個步驟即可使用。

1. 點擊本儲存庫頂部的 `WiiFitToVRC.exe` 下載(無需安裝)。
2. 雙擊下載的檔案即可執行。
3. 應用程式啟動後會自動開始搜尋。只需按下 Wii 平衡板電池盒內的 **SYNC** 按鈕即可自動連接(無需點擊連接按鈕)。
4. 依照畫面提示操作(**キャリブレーション(校準)** → 走下平衡板等待 → 重新站上平衡板等待)即可完成準備。之後啟動 VRChat,在平衡板上移動體重即可行走。

提示:若要確認連接是否正常,可以開啟記事本並踩上平衡板 —— 如果輸入了 w/s/a/d,表示連接正常。若在 VR 模式等情況下遊戲仍然沒有反應,請先開啟 VRChat 本身的 OSC 功能,再嘗試在設定中將輸出方式切換為 VRChat OSC。

若無法正常運作,可參考 [docs](docs/) 資料夾(英文)中各功能的詳細說明。

改善點·修正意見請聯絡製作者的 X:[@nyamo_chi](https://x.com/nyamo_chi)

## 特色

- **無需輸入 PIN 即可完成藍牙配對** — 原理請參見 [docs/BALANCE_BOARD.md](docs/BALANCE_BOARD.md)(英文)。
- **兩階段校準**:一次性的感測器零點校準(需走下平衡板進行),以及在背景持續自動更新的「基準體重」(即使換人站上去也能立刻跟上)。
- **前進、後退、衝刺、左右轉向、跳躍、蹲下動作偵測** — 各項判定邏輯及可調整設定請參見 [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md)(英文)。
- **四種輸出模式**:
  - 鍵盤(轉向使用 Q/E 鍵)
  - 鍵盤+滑鼠(轉向使用滑鼠視角移動 — 預設)
  - 虛擬 Xbox 360 手把 — 適用於會拒絕 SendInput 合成鍵盤/滑鼠輸入的遊戲(包括 VRChat)。詳情請參見 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)(英文)。
  - 使用 VRChat 的 OSC 功能 — 適用於 VR 裝置鎖定輸入、連虛擬手把在內的一切合成輸入都無法接受的環境。詳情請參見 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)(英文)。
- 按鍵綁定/手把分配、轉向靈敏度、重量閾值、各類時間參數等均可在應用程式內設定視窗中細部調整。
- 多語言介面:自動偵測 Windows 顯示語言,內建日文、英文、簡體/繁體中文、韓文、法文、德文、義大利文。

## 也可用於其他遊戲

本應用程式的輸出是一般的鍵盤 WASD(或滑鼠)輸入,因此即使沒有官方支援,只要遊戲支援 WASD 移動,就能在其他以行走為主的遊戲中使用。已嘗試可用的例子:

- Death Stranding
- Resident Evil
- Monster Hunter
- Armored Core IV

## 執行環境

- Windows 10/11
- Wii 平衡板(藍牙)— 已停產,但在二手市場很容易以低價購得
- 支援 HID 裝置的藍牙轉接器

### 使用虛擬手把時(可選)

[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)(這是一個真實的核心驅動程式 — 本應用程式無法自動為您安裝,請自行下載並安裝)

## 從原始碼建置

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```
dotnet build WiiFitToVRC.sln
```

若要產生部署在儲存庫根目錄的自我完備單一 exe:

```
powershell -File publish.ps1
```

## 專案結構

```
WiiFitToVRC.exe          預先建置的自我完備執行檔(由 publish.ps1 產生)
publish.ps1               重新建置並重新部署 WiiFitToVRC.exe 的腳本
src/
  WiiFitToVRC.Core/        核心邏輯:藍牙配對、HID 通訊、動作偵測、
                           設定、多語言化、輸出(鍵盤/滑鼠/手把/OSC)
  WiiFitToVRC.App/         WinForms 介面(監視視窗 + 設定對話框)
tools/
  PairTool/                單獨測試平衡板配對的主控台工具
  ClassifyTest/             離線重播工具:對錄製的 CSV 記錄檔重新執行判定邏輯,
                           無需實機即可調整閾值
reference/
  WiiBalanceWalker_v0.4/    InTheHand.Net.Personal.dll(32feet.NET),用於藍牙裝置管理
                           — 版權說明請參見附帶的 README.txt
docs/                      (目前僅提供英文版)
  BALANCE_BOARD.md          平衡板藍牙/HID 協定詳情
  GESTURE_DETECTION.md      各動作的判定方式及相關調整設定
  VRCHAT_INPUT.md           一般 SendInput 在 VRChat 中無效的原因,以及三種解決方案
```

## 設定參考

所有設定均可在應用程式內設定視窗(⚙ 設定)中編輯,並儲存到與 exe 同目錄下的 `settings.json` 中。無需手動編輯,以下是概要:

| 設定項目 | 作用 |
|---|---|
| 輸出方式 | 鍵盤 / 鍵盤+滑鼠 / 虛擬手把 / VRChat OSC(詳見 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)) |
| 語言 | 介面顯示語言,也可設為自動跟隨 Windows 設定 |
| 反應靈敏度(轉向/跳躍/蹲下) | 三者可分別獨立用「弱」~「強」滑桿調整(不影響前進/後退)。中間(預設值)保持原有判定基準不變 |
| 轉向靈敏度 | 滑鼠移動量(鍵盤+滑鼠模式)或搖桿偏移%(手把模式),左右可分別設定 |
| 觸發所需的重量閾值 | 判定為「有人站在平衡板上」的校準後總重量 |
| 休眠/喚醒所需秒數 | 輸出鎖定/解除鎖定前需要維持的時間(雙向共用) |
| 踏步閾值(%) | 相對於學習到的基準體重,某一角需超出多少才判定為一次踏步 — 詳見 [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md) |
| 衝刺判定(ms) | 踏步間隔短於該值時判定為衝刺 |
| 步幅(ms) | 偵測到踏步後,在沒有下一次踏步的情況下回到 Idle 狀態之前的持續時間 |
| 蹲下/跳躍啟用 | 可分別關閉各動作(完全停用按鍵輸出及指示燈) |
| 轉向動作啟用 | 關閉後將完全不偵測轉向,無論鍵盤、滑鼠、手把還是 OSC 輸出模式都不會傳送任何相當於左右轉向的動作(不影響前進/後退/衝刺) |
| 偵錯模式 | 顯示用於為 `ClassifyTest` 錄製記錄的原始資料記錄控制項 |
| 按鍵綁定分頁 | 鍵盤輸出模式下各動作對應的按鍵(包括衝刺修飾鍵) |
| 手把分頁 | 虛擬手把模式下各動作對應的按鈕及搖桿偏移量 |

## 授權

本專案自身程式碼採用 [MIT](LICENSE) 授權。附帶的 `InTheHand.Net.Personal.dll` 是第三方函式庫(32feet.NET)— 版權說明請參見 [reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt](reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt)。
