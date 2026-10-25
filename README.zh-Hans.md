# Mom Pet

[繁體中文](README.md) | **简体中文** | [English](README.en.md) | [日本語](README.ja.md)

一只会跟着鼠标跑的 Windows 桌面宠物~！光标变成什么样子，她就换什么表情；点她会跳，一直点会生气，点太多还会晕过去哦！鼠标不动的话她就开始发呆啦。她是透明的，点击会直接穿过去，完全不会挡到你做事情~

<p align="center"><img src="docs/media/follow.gif" width="720" alt="跟着光标跑"></p>

> 内置三个角色：吃白饭的蓝色大肥鱼 DeepSeek娘、捧着星芒想事情的 Claude小姐，还有我自己的 OC 海茵！在设置里就能换~

## 下载

到 [Releases](../../releases) 下载 zip，解压后点 `MomPet.exe` 就好啦！Windows 10 / 11 都能直接用，不用另外装东西。第一次打开如果弹出「Windows 已保护你的电脑」，点「更多信息」再点「仍要运行」就好~

## 她会做什么

**跟着光标跑**：往哪边跑身体就往哪边歪，跑越快身体压越扁，停下来会晃一晃。甩得够快的话她会飘起来~

<p align="center"><img src="docs/media/float.gif" width="720" alt="飘起来"></p>

**被点**：点一下她会跳起来，旁边冒出星星和爱心。在同一个地方一直点（每下隔不到 0.8 秒），她的表情会越来越不妙：

| 连点 | 表情 |
| --- | --- |
| 1 | 开心 `happy` |
| 3 | 吃惊 `surprised` |
| 5 | 慌张 `flustered` |
| 8 | 不耐烦 `annoyed` |
| 11 | 生气 `angry`，会发抖 |
| 16 | 晕了 `dizzy`，转圈圈 |
| 点到 8 下以上再停手 | 闹别扭 `sulk` 一小会儿 |

在链接、按钮上点的时候，她最多只会吃惊，平时上网不会把她惹毛啦~

<p align="center"><img src="docs/media/clicks.gif" width="480" alt="连点"></p>

**发呆**：鼠标 8 秒不动，她就开始做自己的事：DeepSeek娘捧着一大碗白饭吃，Claude小姐捧着橘色星芒想事情，海茵会打瞌睡、头上飘 z。一动鼠标她会被吓一跳！

<p align="center"><img src="docs/media/idle.gif" width="420" alt="睡着和吓醒"></p>

**换表情**：Windows 的 16 种光标每种都有对应的表情。转圈圈的时候她在忙，变成手指的时候她在指东西，禁止符号的时候她会打叉叉~

<p align="center"><img src="docs/media/poses.gif" width="380" alt="光标对应的表情"></p>

全部 26 张长这样（下面的名字就是图片包里的文件名）：

<p align="center"><img src="docs/media/poses.png" alt="全部表情"></p>

## 设置

右下角系统托盘的黄猫点一下就能打开设置！可以换角色、暂停、开机自启、截图和录屏时把她藏起来、鼠标不动一阵子就让她消失、调大小（50%～200%），界面有繁中、简中、English、日本語。全屏玩游戏或放 PPT 的时候，她会自己躲起来哦~

## 换成你自己的角色

准备好透明背景的 PNG，在设置里换上去就好啦！会动的 GIF 也可以哦~ 只放一张 `default.png` 也能用，没画的表情都会用它代替。整套图片也能打包成 zip 分享给别人，动画可以用 `idle_1.png`、`idle_2.png` 这样一帧一帧放进去。想用 NovelAI 或 ChatGPT 画一整套表情的话，看这里：[角色图片创作指南](docs/角色圖片創作指南.md)（繁体中文）

## 自己做点击特效

点击特效是一个写公式的文本文件，不用改程序就能做出自己的特效~ 内置两种：

<p align="center"><img src="docs/media/effects.gif" width="520" alt="内置点击特效"></p>

写法在这里：[点击特效说明](docs/點擊特效說明.md)（繁体中文）

## 自己编译

只要 Windows 自带的 .NET Framework 4，不用装 Visual Studio。在文件夹里运行 `build.cmd` 就会生成 `MomPet.exe`，运行的时候要跟 `assets`、`characters`、`lang`、`effects` 四个文件夹放在一起。

想多加一个内置角色，就在 `characters` 里建一个文件夹放图片，再写一个 `character.txt`，可以设置名字、作者、大小、动画每帧的时间，写法看 `src\Characters.cs` 最上面的说明~

加上 `--settings` 会一打开就显示设置页，加上 `--stats` 会每 5 秒把帧率写进 exe 旁边的 `stats.log`。设置存在 `HKCU\Software\MomPet`，自定义的图片和特效存在 `%LOCALAPPDATA%\MomPet\`。

## 许可

代码是 [MIT](LICENSE)，随便用~

图片（`characters`、`assets`、`docs/media` 里的）是 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)：要署名、不能商用、改了要用一样的许可分享哦！

DeepSeek娘是社区的二创角色，不是 DeepSeek 官方的设定。原型是上善无形的「溟月」，女仆鲸鱼娘是 ZipZipPipe 画的，详细看 [characters/deepseek/LICENSE.txt](characters/deepseek/LICENSE.txt)。Claude小姐也是粉丝角色，不是 Anthropic 官方的，角色设计是 ZipZipPipe，看 [characters/claude/LICENSE.txt](characters/claude/LICENSE.txt)。
