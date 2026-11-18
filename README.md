# ⚡ RealiTLScanner 图形界面

RealiTLScanner 的现代暗色主题 Windows 图形界面——这是一款强大的 TLS 1.3 扫描器，用于发现可行的 REALITY 目标。

> **注意：** 本 GUI 为原始命令行扫描器提供了图形化前端，所有扫描逻辑均未改动。

---

## ✨ 功能特性

- **三种扫描模式** — 扫描 IP / CIDR / 域名、从文件读取、或从 URL 爬取域名
- **完整的 CLI 选项** — 端口、线程数、超时、输出文件、详细输出、IPv6 支持
- **实时日志** — 彩色输出（🟢 可行结果、🔴 错误、🟣 命令）
- **结果表格** — 可排序的数据表格，包含 IP、来源、证书域名、证书颁发者、地理位置
- **右键复制** — 复制 IP、域名、行或全部结果到剪贴板
- **GeoIP 自动下载** — 自动下载并每 7 天更新 `Country.mmdb`
- **CSV 导出** — 将扫描结果导出为 CSV 文件
- **深色主题** — 高品质深色 UI，自定义滚动条、样式化输入框和流畅视觉效果
- **开始 / 停止** — 随时启动或停止扫描
- **计时器** — 显示扫描运行时长

---

## 📸 截图

<img width="1359" height="967" alt="image" src="https://github.com/user-attachments/assets/ae9ba6f9-5a83-4ad4-bb12-a0b52507a149" />

---

## 📋 系统要求

- **Windows 10/11**
- **.NET 9.0 运行时** — [在此下载](https://dotnet.microsoft.com/download/dotnet/9.0)
- `Core.exe` — 已包含在项目目录中

---

## 🚀 快速开始

1. 下载最新发行版或自行编译
2. 运行 `RealiTLScannerGUI.exe`（扫描器可执行文件已包含）
3. 输入目标（IP、CIDR 或域名），点击 **▶ 开始**

首次启动时会自动下载 GeoIP 数据库（`Country.mmdb`）。

---

## 🔧 从源码编译

### 依赖框架（需要 .NET 运行时，约 615KB）

```bash
cd RealiTLScannerGUI
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

### 独立部署（无需运行时，约 70MB）

```bash
cd RealiTLScannerGUI
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish-standalone
```

---

## 🛠️ CLI 参数对照

GUI 将所有原始命令行标志映射为直观的控件：

| CLI 参数 | GUI 控件 | 默认值 |
|----------|---------|--------|
| `-addr` | IP / CIDR / 域名 输入框 | — |
| `-in` | 文件选择器（浏览按钮） | — |
| `-url` | URL 输入框 | — |
| `-port` | 端口数字框 | `443` |
| `-thread` | 线程数字框 | `8` |
| `-timeout` | 超时数字框 | `10` |
| `-out` | 输出文件路径 | `out.csv` |
| `-v` | 详细输出 复选框 | `off` |
| `-46` | 启用 IPv6 复选框 | `off` |

---

## 🙏 致谢

本 GUI 基于原版 **[RealiTLScanner](https://github.com/XTLS/RealiTLScanner)** 开发，完全归功于其作者提供的核心扫描引擎。本项目仅为该工具增加了图形化前端，使其更易于使用。

---

## 📄 许可证

本项目遵循与原 [RealiTLScanner](https://github.com/XTLS/RealiTLScanner) 相同的许可证。