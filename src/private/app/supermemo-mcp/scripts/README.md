# Windows 菜单信息监控器

这个目录包含用 Python 实现的 Windows 菜单信息获取工具，功能等同于提供的 AutoHotkey 脚本。

原版 AutoHotkey 脚本见 [Get Info from Context Menu (x64/x32 compatible)](https://www.autohotkey.com/boards/viewtopic.php?t=31971)

## 功能特性

这个工具实现了与 AutoHotkey 脚本相同的所有功能：

- 实时监控鼠标下方的标准 Windows 弹出菜单（类名为 `#32768`）
- 获取每个菜单项的详细状态信息（启用、选中、默认、禁用、灰色、高亮）
- 获取每个菜单项的文本内容和唯一 ID
- 在控制台实时显示格式化的菜单信息

## 技术实现

### 核心技术栈

- **Win32 API**: 使用 `ctypes` 调用 Windows API
- **菜单检测**: 通过窗口类名 `#32768` 识别标准弹出菜单
- **信息获取**: 使用 `GetMenuItemInfo`、`GetMenuItemID` 等 API

### 关键 API 调用

1. `GetCursorPos` - 获取鼠标位置
2. `WindowFromPoint` - 根据坐标获取窗口句柄
3. `GetClassName` - 获取窗口类名（判断是否为弹出菜单）
4. `SendMessage(MN_GETHMENU)` - 获取菜单句柄
5. `GetMenuItemCount` - 获取菜单项数量
6. `GetMenuItemInfo` - 获取菜单项详细信息
7. `GetMenuItemID` - 获取菜单项 ID

### 状态标志位

实现了完整的菜单状态检测：

- `MFS_ENABLED` (0) - 启用状态
- `MFS_CHECKED` (8) - 选中状态
- `MFS_DEFAULT` (0x1000) - 默认项
- `MFS_DISABLED` (2) - 禁用状态
- `MFS_GRAYED` (1) - 灰色状态
- `MFS_HILITE` (0x80) - 高亮状态

## 使用方法

### 运行菜单监控器

```bash
# 在项目根目录下
uv run --package supermemo-mcp python scripts/menu_info_monitor.py
```

### 测试步骤

1. 运行脚本后，在桌面或文件资源管理器中右击，弹出上下文菜单
2. 将鼠标悬停在菜单上
3. 观察控制台输出的菜单项信息
4. 按 `Ctrl+C` 退出程序

## 输出示例

```text
=== 菜单状态信息 ===
1: Enabled
2: Enabled
3: Enabled
4: Grayed
5: Enabled Default

=== 菜单文本和ID ===
1: 打开 - ID=40001
2: 编辑 - ID=40002
3: 复制 - ID=40003
4: 删除 - ID=40004
5: 属性 - ID=40005
```

## Comparison with the AutoHotkey Version

| Feature              | AutoHotkey Version | Python Version    |
| -------------------- | ------------------ | ----------------- |
| Menu detection       | Yes                | Yes               |
| State retrieval      | Yes                | Yes               |
| Text retrieval       | Yes                | Yes               |
| ID retrieval         | Yes                | Yes               |
| Real-time monitoring | Yes                | Yes               |
| Cross-platform       | No (Windows only)  | No (Windows only) |
| Dependencies         | AHK runtime        | Python 3.12+      |

## 技术说明

### 技术架构

Python 版本采用面向对象设计，主要包含：

- `MenuItemInfo` 数据类：封装菜单项的状态、文本和 ID 信息
- `MenuInfoMonitor` 监控器类：负责实时监控和信息获取
- `WindowsAPI` 封装类：提供统一的 Windows API 接口

### 错误处理

- API 调用失败检测
- 内存分配错误处理
- 优雅的异常处理和资源清理

### 性能优化

- 避免不必要的 API 调用
- 智能输出更新（仅在内容变化时刷新）
- 合理的轮询间隔（500ms）

## 适用场景

1. **菜单自动化**: 获取特定菜单项的 ID 用于自动化操作
2. **UI 测试**: 验证应用程序菜单的正确性
3. **辅助功能**: 为屏幕阅读器等辅助工具提供菜单信息
4. **系统监控**: 监控和记录用户的菜单操作
5. **逆向工程**: 分析第三方应用程序的菜单结构

## 注意事项

- 仅适用于标准 Windows 弹出菜单（类名 `#32768`）
- 需要管理员权限访问某些系统菜单
- 某些应用程序使用自定义菜单实现，可能无法检测
- Python 版本需要 Windows 平台和 Python 3.12+
