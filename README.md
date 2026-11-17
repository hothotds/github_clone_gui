# GitHub 克隆 / 下载工具

一个 Windows 小工具：粘贴 GitHub 仓库地址就能 `git clone`，粘贴文件直链（Releases、源码压缩包等）就能下载，zip 还会自动解压。不用记命令，带进度条，可随时取消。

## 功能

- **自动识别输入类型**：仓库地址走 Git 克隆，文件链接走下载，界面上会显示“识别为”什么。
- **支持简写**：直接输入 `user/repo`，自动补全为 `https://github.com/user/repo.git`。
- **Git 克隆**：显示克隆各阶段的实时进度（Receiving objects、Resolving deltas 等），可取消。
- **文件下载**：显示已下载大小、总大小和速度；先下载到 `.part` 临时文件，完成后再改名，中途失败或取消不会留下残缺文件；支持重定向。
- **zip 自动解压**：解压到与压缩包同名的文件夹。如果压缩包里所有内容都在同一个顶层文件夹（GitHub 源码压缩包就是这样），会自动去掉这一层，避免出现 `repo\repo-main\` 这样的嵌套。解压完成后可选择是否删除压缩包。
- **名称可改**：默认使用仓库名或文件名，也可以手动改成想要的名字；界面会显示“最终路径”。
- **安全检查**：解压时会拦截试图跳出目标文件夹的压缩包条目；目标已存在时会提示，不会悄悄覆盖。
- **记住设置**：保存目录、“解压后删除压缩包”选项会在下次打开时自动恢复。
- 任务完成后，点「打开文件夹」直接在资源管理器里定位结果。

## 下载与运行

到 [Releases](../../releases) 页面下载 `Github_Download_Gui.exe`，双击即可运行，不需要安装。

**运行要求**

- Windows 7 SP1 / 8 / 10 / 11
- .NET Framework 4.8（Windows 10 1903 及以上、Windows 11 已自带；更早的系统需要自行安装）
- 使用「Git 克隆」功能需要已安装 [Git](https://git-scm.com/downloads)，并且 `git` 在系统 `PATH` 中。只下载文件不需要 Git。

## 使用方法

1. 在「地址」里粘贴链接或仓库名。
2. 在「保存到」里选择保存位置，需要的话修改下面的名称。
3. 点「开始」。进度和日志会显示在下方，想中止就点「取消」。
4. 完成后点「打开文件夹」。

## 注意事项

- **私有仓库**：克隆时程序不会弹出账号密码输入框（已关闭 Git 的终端提示，避免界面卡住）。需要提前在本机配置好 Git 凭据，否则会克隆失败。
- **目标文件夹**：Git 克隆要求目标文件夹不存在或为空；zip 解压到已有的非空文件夹时，会合并并覆盖同名文件，程序会先询问你。
- **代理**：文件下载使用系统代理设置；Git 克隆使用 Git 自己的代理配置（例如 `git config --global http.proxy ...`）。
- **设置文件位置**：`%APPDATA%\GithubDownloadGui\settings.txt`，删除它即可恢复默认设置。

## 从源码构建

需要安装 [.NET SDK](https://dotnet.microsoft.com/download)（项目目标框架为 .NET Framework 4.8）。

```
dotnet build -c Release
```

生成的 exe 位于 `Release\v1.1\Github_Download_Gui.exe`（输出目录跟随项目文件里的版本号）。

项目文件里设置了 `ApplicationIcon` 为 `app.ico`，构建前请在项目目录放一个 `app.ico`，或者删掉 `github_download_gui.csproj` 里的这一行。

## 项目结构

| 文件 | 说明 |
| --- | --- |
| `Program_1.cs` | 程序入口 |
| `MainForm.cs` | 界面、Git 克隆、下载与解压逻辑 |
| `Util.cs` | 输入类型识别、地址补全、文件名推断等工具函数 |
| `github_download_gui.csproj` | 项目文件 |
