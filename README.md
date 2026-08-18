# DSH Web Launcher

轻量 Windows 托盘工具，用于启动、停止和监控本机 `dsh web`。

## 能力

- 一键启动和停止由本应用管理的完整 `dsh web` 进程树
- 校验 DSH 专属页面标识，避免把同端口的普通 Web 服务误报为 DSH
- 显示 Node 服务 PID、进程树内存和运行时长
- 启动时立即识别并接管由其他终端启动的本机 DSH Web；自动启动前也会先探测，避免重复创建实例
- 通过 DSH 专属健康检查和监听端口 Node PID 双重确认，接管后可查看资源、停止实例或随启动器退出
- 如果无法安全定位本机进程，则降级为只读外部状态，不执行停止操作
- 启动参数持久化：命令、监听地址、端口、Trusted Host、额外参数
- 运行中修改参数会明确提示“下次启动生效”
- 托盘鲸鱼图标：未运行时白色，服务响应时蓝色
- 托盘菜单会根据受管、外部和停止失败状态动态启用
- 单实例运行；再次启动会唤醒已有窗口
- 关闭窗口默认收进托盘，可选开机启动和应用启动时自动启动 DSH
- 损坏配置会自动备份为 `settings.invalid.<时间>.json`
- 固定 300 行日志缓冲，保持常驻占用稳定

## 构建与测试

```powershell
dotnet build DshWebLauncher.sln -c Release
dotnet test tests\DshWebLauncher.Tests\DshWebLauncher.Tests.csproj -c Release
```

## 发布

一次生成轻量版、独立版、ZIP 和 SHA-256：

```powershell
.\build-release.ps1 -Mode All
```

- `lightweight`：单文件体积小，需要目标电脑安装 .NET 8 Desktop Runtime。
- `standalone`：包含运行时，无需另行安装 .NET，体积较大。

也可用 `-Mode Lightweight` 或 `-Mode Standalone` 只生成一种。产物位于 `artifacts\release`。

应用默认调用 PATH 中的 `dsh.cmd`，也可以在设置中填写完整路径。配置保存于 `%LocalAppData%\DshWebLauncher\settings.json`。

## 资源说明

`src/DshWebLauncher/Assets/deepseek-whale.svg` 取自当前安装的 DSH Web 前端 `favicon.svg`，并由项目内的 `tools/IconBuilder` 生成 Windows ICO 托盘资源。
