# 源码与构建来源

- 产品：轻挂 AirPeg，版本 1.8.5（预览）
- 预构建 EXE 源码检查点：`652a476f6a525845dda9973a92cc32a8523beaca`
- 发布准备开始的本地 HEAD：`8acfc41a207048126a969da0147405eb96d9b40b`
- Windows 基础：[mohabujubara/snapline](https://github.com/mohabujubara/snapline)
- 原作者设计与交互来源：[alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero)

本仓库以经过隐私审查的源码快照建立发布分支。原开发 Git 仓库、完整历史、P1–P6 检查点、旧交付物和本地完整回退包都保留，没有重置或强制推送原开发分支。

仅上传必要产品源码、原生桥接、品牌资源、许可证、通用构建脚本与发布文档。历史测试截图、个人路径记录、开发日志、旧品牌图片、缓存及未经公开审查的素材不进入此快照及其可达历史。产品 C# / XAML / Native C++ / HLSL 文件保留与预构建版本相同的字节，见 [文件校验记录](docs/source-files.sha256)。

构建基础设施的唯一适配是现有 MSVC/SDK 自动定位、仓库内构建日志与缓存，以及 AirPeg 便携包入口。没有更改产品动画、截图编辑、玻璃视觉或业务逻辑。

预构建便携 ZIP 和已审核的完整视频独立作为预览 Release 附件，不写入源码 Git 历史。视频保持原文件字节，没有重新补片头。源码快照的 Git 提交号与预构建二进制的开发提交后缀不同，请以本记录和附件 SHA-256 核对。
