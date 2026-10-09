# 設定介面素材

`settings-landscape.png` 是為此 GameHelper2 個人介面新生成的原創底圖。使用內建 imagegen 工具，2026-09-24。使用者提供的啟動器截圖只作為介面風格參考，程式中的排版、文字和互動由 ImGui 實際繪製。

此圖由 `GameHelper.csproj` 隨編譯輸出到 `Assets`，由設定頁載入一次並交由 Overlay 管理材質。載入失敗時保留純色背景與所有設定功能。

## 完整生成提示詞

Use case: stylized-concept. Asset type: original landscape background for a desktop ARPG companion settings launcher. Wide 16:9 panoramic blue fantasy landscape, elegant painterly high-end game concept art. Misty turquoise mountain valleys, distant ancient stone arches and ruined observatory concentrated on the right, tiny warm golden magical light near a stone gateway, soft atmospheric clouds. Left half predominantly quiet dark desaturated navy blue mist and open sky, suitable for white UI text. Cool teal and slate blue palette with subtle warm light, tranquil exploration mood, low contrast, refined depth. No characters, no creatures, no text, no typography, no logos, no UI, no border, no watermark. It is a background image only, no mockup. Original environment, not based on any existing game location.

## 介面實作

- `Ui/PlayerSettingsShell.cs`：玩家分類導航、首頁、搜尋、插件入口、退出確認。
- `Ui/ImGuiTheme.cs`：全域色彩與圓角樣式。
- `Settings/SettingsWindow.cs`：串接插件生命週期、設定頁與儲存事件。

退出彈窗採用深色面板及背景遮罩；沒有套用即時模糊效果。介面預覽在 Tests/artifacts/settings-zh-Hant，使用 Tests/SettingsPreview 渲染正式元件，不是概念圖。
