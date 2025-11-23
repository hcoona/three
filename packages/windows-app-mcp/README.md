# Windows App MCP

This service packed several Windows API into MCP to allow AI agents interoperates with Windows applications.

## Basic Idea

Reference AutoHotKey (AHK).

### 窗口与程序管理（Window & Process Management）

窗口操作：激活、关闭、隐藏、显示、移动、调整大小、设置置顶等。

窗口检测：查找窗口、检测窗口是否存在、获取窗口属性（如句柄、标题、PID等）。

多窗口管理：同时操作多个窗口、窗口分组等。

程序启动与管理：启动外部程序、关闭进程、检测进程是否运行、切换程序窗口等。

窗口消息处理：发送/接收窗口消息（SendMessage/PostMessage）。

### 输入模拟与控制（Input Simulation & Control）

键盘操作：发送按键（支持组合键、长按、连按）、文本输入、键盘钩子（监控/拦截键盘事件）。

鼠标操作：点击、双击、右击、拖拽、滚轮、移动到指定坐标等。

手势/热键注册：全局/局部快捷键绑定、鼠标手势、快捷键触发脚本。

### 文本与数据处理（Text & Data Manipulation）

剪贴板操作：读取、设置、监听剪贴板变化。

文本处理：搜索、替换、正则表达式、格式化文本。

自动填充表单：智能填充、批量填写。

数据导入导出：如CSV/Excel文件读写。

数据存储：变量持久化、配置文件读写（INI/JSON/XML等）。

### 系统与设备控制（System & Device Control）

系统命令：关机、重启、锁屏、注销。

系统托盘操作：自定义系统托盘图标、菜单、提示。

系统设置调整：音量、亮度、显示器、分辨率切换。

多显示器管理：显示器间窗口移动、切换主显示器等。

虚拟桌面支持。

### UI 自动化与高级交互（UI Automation & Advanced Interaction）

控件自动化：直接操作窗口控件（如按钮、输入框、下拉框），读取/设置值。

图像识别与OCR：截屏后图像查找、图片匹配、文本识别。

菜单与对话框操作：自动点击菜单项、处理弹窗（如确定/取消按钮）。

通知与弹窗管理：自动关闭、捕获系统通知。

### 事件与脚本逻辑（Events & Script Logic）

定时与计划任务：定时执行、循环操作、延迟等待。

事件触发器：如文件变化、窗口变化、网络事件等触发操作。

条件与流程控制：if/else、循环、跳转、函数调用、错误处理等。

多线程/并发控制（部分高级需求）。

插件/扩展支持：加载自定义 DLL/COM/脚本。

### 用户通知与界面（User Notification & UI）

消息框/气泡提示：弹出消息提示用户。

自定义UI窗口：如输入框、选择对话框、进度条。
