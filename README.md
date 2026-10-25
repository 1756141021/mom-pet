# Mom Pet

**繁體中文** | [简体中文](README.zh-Hans.md) | [English](README.en.md) | [日本語](README.ja.md)

一隻會跟著滑鼠跑的 Windows 桌面寵物~！游標變成什麼樣子，她就換什麼表情；點她會跳，一直點會生氣，點太多還會暈過去哦！滑鼠不動的話她就開始發呆啦。她是透明的，點擊會直接穿過去，完全不會擋到你做事情~

<p align="center"><img src="docs/media/follow.gif" width="720" alt="跟著游標跑"></p>

> 內建三個角色：吃白飯的藍色大肥魚 DeepSeek娘、捧著星芒想事情的 Claude小姐，還有我自己的 OC 海茵！在設定頁就能換~

## 下載

到 [Releases](../../releases) 下載 zip，解壓縮後點 `MomPet.exe` 就好啦！Windows 10 / 11 都能直接用，不用另外裝東西。第一次打開如果跳出「Windows 已保護您的電腦」，按「其他資訊」再按「仍要執行」就好~

## 她會做什麼

**跟著游標跑**：往哪邊跑身體就往哪邊歪，跑越快身體壓越扁，停下來會晃一晃。甩得夠快的話她會飄起來~

<p align="center"><img src="docs/media/float.gif" width="720" alt="飄起來"></p>

**被點**：點一下她會跳起來，旁邊冒出星星和愛心。在同一個地方一直點（每下隔不到 0.8 秒），她的表情會越來越不妙：

| 連點 | 表情 |
| --- | --- |
| 1 | 開心 `happy` |
| 3 | 吃驚 `surprised` |
| 5 | 慌張 `flustered` |
| 8 | 不耐煩 `annoyed` |
| 11 | 生氣 `angry`，會發抖 |
| 16 | 暈了 `dizzy`，轉圈圈 |
| 點到 8 下以上再停手 | 鬧彆扭 `sulk` 一下下 |

在連結、按鈕上點的時候，她最多只會吃驚，平常上網不會把她惹毛啦~

<p align="center"><img src="docs/media/clicks.gif" width="480" alt="連點"></p>

**發呆**：滑鼠 8 秒不動，她就開始做自己的事：DeepSeek娘捧著一大碗白飯吃，Claude小姐捧著橘色星芒想事情，海茵會打瞌睡、頭上飄 z。一動滑鼠她會被嚇一跳！

<p align="center"><img src="docs/media/idle.gif" width="420" alt="睡著和嚇醒"></p>

**換表情**：Windows 的 16 種游標每種都有對應的表情。轉圈圈的時候她在忙，變成手指的時候她在指東西，禁止符號的時候她會打叉叉~

<p align="center"><img src="docs/media/poses.gif" width="380" alt="游標對應的表情"></p>

全部 26 張長這樣（下面的名字就是圖片包裡的檔名）：

<p align="center"><img src="docs/media/poses.png" alt="全部表情"></p>

## 設定

右下角系統匣的黃貓點一下就能打開設定！可以換角色、暫停、開機自動啟動、截圖和錄影時把她藏起來、滑鼠不動一陣子就讓她消失、調大小（50%～200%），介面有繁中、简中、English、日本語。全螢幕玩遊戲或放簡報的時候，她會自己躲起來哦~

## 換成你自己的角色

準備去背的 PNG，在設定頁換上去就好啦！會動的 GIF 也可以哦~ 只放一張 `default.png` 也能用，沒畫的表情都會用它代替。整套圖片也能打包成 zip 分享給別人，動畫可以用 `idle_1.png`、`idle_2.png` 這樣一格一格放進去。想用 NovelAI 或 ChatGPT 畫一整套表情的話，看這裡：[角色圖片創作指南](docs/角色圖片創作指南.md)

## 自己做點擊特效

點擊特效是一個寫公式的文字檔，不用改程式就能做出自己的特效~ 內建兩種：

<p align="center"><img src="docs/media/effects.gif" width="520" alt="內建點擊特效"></p>

寫法在這裡：[點擊特效說明](docs/點擊特效說明.md)

## 自己編譯

只要 Windows 內建的 .NET Framework 4，不用裝 Visual Studio。在資料夾裡執行 `build.cmd` 就會產生 `MomPet.exe`，執行的時候要跟 `assets`、`characters`、`lang`、`effects` 四個資料夾放在一起。

想多加一個內建角色，就在 `characters` 裡開一個資料夾放圖片，再寫一個 `character.txt`，可以設定名字、作者、大小、動畫每格的時間，寫法看 `src\Characters.cs` 最上面的說明~

加上 `--settings` 會一打開就顯示設定頁，加上 `--stats` 會每 5 秒把幀率寫進 exe 旁邊的 `stats.log`。設定存在 `HKCU\Software\MomPet`，自訂的圖片和特效存在 `%LOCALAPPDATA%\MomPet\`。

## 授權

程式碼是 [MIT](LICENSE)，隨便用~

圖片（`characters`、`assets`、`docs/media` 裡的）是 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)：要署名、不能商用、改了要用一樣的授權分享哦！

DeepSeek娘是社群的二創角色，不是 DeepSeek 官方的設定。原型是上善无形的「溟月」，女僕鯨魚娘是 ZipZipPipe 畫的，詳細看 [characters/deepseek/LICENSE.txt](characters/deepseek/LICENSE.txt)。Claude小姐也是粉絲角色，不是 Anthropic 官方的，角色設計是 ZipZipPipe，看 [characters/claude/LICENSE.txt](characters/claude/LICENSE.txt)。
