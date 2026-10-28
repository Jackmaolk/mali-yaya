# 芽芽桌面图标与名称

日期：2026-10-28；版本：2026.10.28.3。

## 来源与输出

使用 Codex 内置 image_gen 工具新生成的原创叶片机器人图标，不是从网上下载的角色图。设计保持薄荷绿头壳、深青电子脸、叶片、杏色机身和菱形胸灯，避免旧猫型角色特征。

- Assets/mali-yaya.png：生成的原始透明 PNG。
- Assets/mali-yaya.ico：通过 scripts/Build-Icon.ps1 转换的 Windows 多尺寸资源，含 16/20/24/32/40/48/64/128/256 像素。
- ICO 嵌入 WPF 程序、原生 WinExe 启动器及桌面入口安装器；没有把图标依赖留在生成工具的缓存目录。

此后再生成/替换素材时，应同步人工审查并更新 SourcePolicy.ps1 的资产 SHA256 指纹。白名单只允许这两个精确图标资源绕过文本扫描，不放行任意图片或历史截图。

## 生成模式与完整提示词

模式：内置 image_gen，新图生成，transparent_background=true；未使用 API Key 或 CLI fallback。以下为实际生成提示词：

~~~text
Create ONE polished cute Windows desktop app icon for an original app called Mali Yaya (码力芽芽). Image ONLY, no writing, no letters, no numbers, no mockup. A newly designed friendly leaf-sprout robot: mint-green rounded rectangular monitor-shaped head, deep dark-teal screen face, two large luminous mint-white round eyes and a simple smiling mouth, two asymmetrical green leaves sprouting on its head; a tiny apricot capsule torso with a small diamond chest light and dark-teal feet. Main focus is the large expressive face and leaves, with a small suggestion of body, centered and filling most of a square. Friendly, adorable, premium soft 3D clay-and-glass illustration, tasteful dimensional highlights, crisp strong silhouette that remains readable at 16px/32px, clean generous safe margins, no tiny decorative clutter. A soft mint-to-ivory rounded-square squircle app tile behind the character, restrained warm rim lighting and subtle inset shadow. Truly transparent outside the rounded square. Straight-on front view, perfectly centered, square composition. Distinct original plant robot, NOT a cat, NO whiskers, NO bell, NO red collar, NO belly pocket, not resembling Doraemon or any existing franchise.
~~~

## 名称和兼容

中文界面名称“码力芽芽”，英文“Mali Yaya”。发布包内部 CodexUsageWidget.exe/DLL 技术文件名保持稳定，避免破坏更新白名单和旧入口。新的个人桌面入口使用码力芽芽.exe（中文系统）或 Mali Yaya.exe（其他语言系统）。

桌面入口安装器只在用户主动双击后复制启动器，先备份旧入口，再尝试移走旧名；被占用则保留旧入口，不停止正在使用的 Codex 或小组件。构建默认不改桌面，只有显式 -InstallDesktop 才启动入口安装器。当前工具未获桌面写权限，因此不会假称桌面图标已自动替换。

## 权利边界

图标是为本项目新生成的资产；生成和自行设计不构成全球无侵权、商标注册或商用签名保证。完整代码许可和公开发行审查仍独立进行，不复用其他项目许可证作为本项目授权。
