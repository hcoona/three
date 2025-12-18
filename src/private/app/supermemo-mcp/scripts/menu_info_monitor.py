"""
Windows 菜单信息监控器

实现与 AutoHotkey 脚本相同的功能：
- 实时监控鼠标下方的标准 Windows 弹出菜单
- 获取每个菜单项的状态（启用、选中、高亮等）
- 获取每个菜单项的文本内容和 ID
- 在控制台输出菜单信息

作者: Python 版本实现
支持 Python 3.12+
"""

import sys
import time
from dataclasses import dataclass

from supermemo_mcp.windows_api import MenuFlags, WindowsAPI


@dataclass
class MenuInfo:
    """菜单信息数据类"""

    hwnd: int
    hmenu: int
    window_class: str
    items: list["MenuItemInfo"]
    window_info: "WindowInfo"

    @property
    def menu_id(self) -> str:
        """获取菜单的唯一标识符"""
        return f"HWND:{self.hwnd:08X}-HMENU:{self.hmenu:08X}"


@dataclass
class MenuItemInfo:
    """菜单项信息数据类"""

    index: int
    state: int
    text: str
    item_id: int

    def get_state_description(self) -> str:
        """
        获取菜单项状态的可读描述。

        Returns:
            str: 状态描述字符串，包含启用、选中、默认等状态信息
        """
        states = []
        if self.state == MenuFlags.MFS_ENABLED:
            states.append("Enabled")
        if self.state & MenuFlags.MFS_CHECKED:
            states.append("Checked")
        if self.state & MenuFlags.MFS_DEFAULT:
            states.append("Default")
        if self.state & MenuFlags.MFS_DISABLED:
            states.append("Disabled")
        if self.state & MenuFlags.MFS_GRAYED:
            states.append("Grayed")
        if self.state & MenuFlags.MFS_HILITE:
            states.append("Highlight")

        return " ".join(states) if states else "Unknown"


@dataclass
class WindowInfo:
    """窗口详细信息数据类"""

    hwnd: int
    class_name: str
    title: str
    rect: tuple[int, int, int, int] | None
    client_rect: tuple[int, int, int, int] | None
    visible: bool
    enabled: bool
    thread_id: int | None
    process_id: int | None
    ancestors: list[tuple[int, str]]
    enhanced_info: dict | None = None

    @property
    def window_size(self) -> tuple[int, int] | None:
        """获取窗口大小"""
        if self.rect:
            return (self.rect[2] - self.rect[0], self.rect[3] - self.rect[1])
        return None

    @property
    def window_position(self) -> tuple[int, int] | None:
        """获取窗口位置"""
        if self.rect:
            return (self.rect[0], self.rect[1])
        return None


class MenuInfoMonitor:
    """菜单信息监控器"""

    def __init__(self):
        self.api = WindowsAPI()
        self.current_menu_id: str | None = None
        self.last_output = ""

    def get_menu_info(self, hwnd: int) -> MenuInfo | None:
        """获取完整的菜单信息"""
        if not self.api.is_popup_menu(hwnd):
            return None

        hmenu = self.api.get_menu_handle(hwnd)
        if not hmenu:
            return None

        count = self.api.get_menu_item_count(hmenu)
        if count <= 0:
            return None

        # 获取窗口类名
        window_class = self.api.get_window_class_name(hwnd)

        # 获取菜单项信息
        menu_items = []
        for i in range(count):
            state = self.api.get_menu_item_state(hmenu, i)
            text = self.api.get_menu_item_text(hmenu, i)
            item_id = self.api.get_menu_item_id(hmenu, i)

            menu_items.append(
                MenuItemInfo(index=i + 1, state=state, text=text, item_id=item_id)
            )

        # 获取窗口详细信息
        window_info = self.get_window_info(hwnd)

        return MenuInfo(
            hwnd=hwnd,
            hmenu=hmenu,
            window_class=window_class,
            items=menu_items,
            window_info=window_info,
        )

    def get_window_info(self, hwnd: int) -> WindowInfo:
        """获取窗口详细信息"""
        comprehensive_info = self.api.get_comprehensive_window_info(hwnd)

        window_class = self.api.get_window_class_name(hwnd)
        title = self.api.get_window_text(hwnd)
        rect = self.api.get_window_rect(hwnd)
        client_rect = self.api.get_client_rect(hwnd)
        visible = self.api.is_window_visible(hwnd)
        enabled = self.api.is_window_enabled(hwnd)
        thread_process_info = self.api.get_window_thread_process_id(hwnd)
        ancestors = self.api.get_window_ancestor_chain(hwnd)

        thread_id = None
        process_id = None
        if thread_process_info:
            thread_id, process_id = thread_process_info

        return WindowInfo(
            hwnd=hwnd,
            class_name=window_class,
            title=title,
            rect=rect,
            client_rect=client_rect,
            visible=visible,
            enabled=enabled,
            thread_id=thread_id,
            process_id=process_id,
            ancestors=ancestors,
            enhanced_info=comprehensive_info,
        )

    def format_output(self, menu_info: MenuInfo) -> str:
        """格式化输出"""
        if not menu_info or not menu_info.items:
            return ""

        lines = []
        lines.append("=== 菜单信息 ===")
        lines.append(f"菜单ID: {menu_info.menu_id}")
        lines.append(f"窗口类: {menu_info.window_class}")
        lines.append(f"项目数: {len(menu_info.items)}")
        lines.append("")

        # 添加窗口详细信息
        window_info = menu_info.window_info
        lines.append("=== 窗口详细信息 ===")
        lines.append(f"窗口句柄: 0x{window_info.hwnd:08X}")
        lines.append(f"窗口类名: {window_info.class_name}")
        lines.append(f"窗口标题: {window_info.title or '(无标题)'}")
        lines.append(f"可见状态: {'是' if window_info.visible else '否'}")
        lines.append(f"启用状态: {'是' if window_info.enabled else '否'}")

        if window_info.window_position and window_info.window_size:
            lines.append(f"窗口位置: ({window_info.window_position[0]}, {window_info.window_position[1]})")
            lines.append(f"窗口大小: {window_info.window_size[0]} × {window_info.window_size[1]}")

        if window_info.thread_id is not None and window_info.process_id is not None:
            lines.append(f"线程ID: {window_info.thread_id}")
            lines.append(f"进程ID: {window_info.process_id}")

        # 添加进程名称
        if window_info.enhanced_info and window_info.enhanced_info.get("process_name"):
            lines.append(f"进程名称: {window_info.enhanced_info['process_name']}")

        # 添加增强信息
        if window_info.enhanced_info:
            enhanced = window_info.enhanced_info
            if enhanced.get("style_description"):
                lines.append(f"窗口样式: {enhanced['style_description']}")
            lines.append("")

            # 显示真正的拥有者窗口信息（应用程序主窗口）
            if enhanced.get("real_owner_hwnd"):
                lines.append("=== 应用程序主窗口信息 ===")
                lines.append(f"主窗口句柄: 0x{enhanced['real_owner_hwnd']:08X}")
                lines.append(f"主窗口类名: {enhanced.get('real_owner_class', '(未知)')}")
                lines.append(f"主窗口标题: {enhanced.get('real_owner_title', '(无标题)')}")
                lines.append("")

            # 显示菜单来源控件信息
            if enhanced.get("source_control_hwnd"):
                lines.append("=== 菜单来源控件信息 ===")
                lines.append(f"来源控件句柄: 0x{enhanced['source_control_hwnd']:08X}")
                lines.append(f"来源控件类名: {enhanced.get('source_control_class', '(未知)')}")
                lines.append(f"来源控件文本: {enhanced.get('source_control_text', '(无文本)')}")
                lines.append("")

            # 显示焦点窗口信息
            if (enhanced.get("focus_window_hwnd") and
                enhanced.get("focus_window_hwnd") != enhanced.get("source_control_hwnd")):
                lines.append("=== 焦点窗口信息 ===")
                lines.append(f"焦点窗口句柄: 0x{enhanced['focus_window_hwnd']:08X}")
                lines.append(f"焦点窗口类名: {enhanced.get('focus_window_class', '(未知)')}")
                lines.append(f"焦点窗口文本: {enhanced.get('focus_window_text', '(无文本)')}")
                lines.append("")

            # 显示菜单拥有者窗口信息
            if (enhanced.get("menu_owner_hwnd") and
                enhanced.get("menu_owner_hwnd") != enhanced.get("real_owner_hwnd") and
                enhanced.get("menu_owner_hwnd") != enhanced.get("source_control_hwnd")):
                lines.append("=== 菜单拥有者窗口信息 ===")
                lines.append(f"菜单拥有者句柄: 0x{enhanced['menu_owner_hwnd']:08X}")
                lines.append(f"菜单拥有者类名: {enhanced.get('menu_owner_class', '(未知)')}")
                lines.append(f"菜单拥有者文本: {enhanced.get('menu_owner_text', '(无文本)')}")
                lines.append("")

            # 显示根拥有者窗口信息
            if (enhanced.get("root_owner_hwnd") and
                enhanced.get("root_owner_hwnd") != enhanced.get("real_owner_hwnd") and
                enhanced.get("root_owner_class")):
                lines.append("=== 根拥有者窗口信息 ===")
                lines.append(f"根拥有者句柄: 0x{enhanced['root_owner_hwnd']:08X}")
                lines.append(f"根拥有者类名: {enhanced.get('root_owner_class', '(未知)')}")
                lines.append(f"根拥有者标题: {enhanced.get('root_owner_title', '(无标题)')}")
                lines.append("")

            # 显示直接拥有者窗口信息
            if (enhanced.get("owner_hwnd") and
                enhanced.get("owner_hwnd") != enhanced.get("real_owner_hwnd") and
                enhanced.get("owner_hwnd") != enhanced.get("root_owner_hwnd")):
                lines.append("=== 直接拥有者窗口信息 ===")
                lines.append(f"拥有者句柄: 0x{enhanced['owner_hwnd']:08X}")
                lines.append(f"拥有者类名: {enhanced.get('owner_class', '(未知)')}")
                lines.append(f"拥有者标题: {enhanced.get('owner_title', '(无标题)')}")
                lines.append("")
        else:
            lines.append("")

        # 添加祖先链信息
        if window_info.ancestors:
            lines.append("=== 窗口祖先链 ===")
            for i, (ancestor_hwnd, ancestor_title) in enumerate(window_info.ancestors):
                level = "  " * i
                title_text = ancestor_title or "(无标题)"
                lines.append(f"{level}├─ 0x{ancestor_hwnd:08X}: {title_text}")
            lines.append("")

        lines.append("=== 菜单状态信息 ===")
        for item in menu_info.items:
            lines.append(f"{item.index}: {item.get_state_description()}")

        lines.append("=== 菜单文本和ID ===")
        for item in menu_info.items:
            text_info = item.text
            # 简化显示格式
            if text_info.startswith("{Owner-drawn:"):
                text_display = "Owner-drawn"
            elif text_info == "{Separator}":
                text_display = "Separator"
            else:
                text_display = text_info

            lines.append(f"{item.index}: {text_display} (ID: {item.item_id})")

        return "\n".join(lines)

    def monitor(self):
        """开始监控"""
        print("菜单信息监控器已启动")
        print("将鼠标悬停在弹出菜单上查看信息")
        print("按 Ctrl+C 退出")
        print("-" * 50)

        try:
            while True:
                x, y = self.api.get_cursor_pos()
                hwnd = self.api.get_window_from_point(x, y)

                if hwnd:
                    menu_info = self.get_menu_info(hwnd)

                    if menu_info:
                        # 检查是否是同一个菜单，避免不必要的刷新
                        if menu_info.menu_id != self.current_menu_id:
                            output = self.format_output(menu_info)

                            # 清屏并显示新信息
                            print("\033[2J\033[H", end="")  # ANSI 清屏
                            print("菜单信息监控器 - 实时显示")
                            print(f"鼠标位置: ({x}, {y})")
                            print("-" * 50)
                            print(output)
                            print("-" * 50)
                            print("按 Ctrl+C 退出")

                            self.current_menu_id = menu_info.menu_id
                            self.last_output = output
                    else:
                        # 没有检测到菜单，清空当前菜单ID
                        if self.current_menu_id is not None:
                            print("\033[2J\033[H", end="")
                            print("菜单信息监控器 - 等待菜单...")
                            print("将鼠标悬停在弹出菜单上查看信息")
                            print("按 Ctrl+C 退出")
                            self.current_menu_id = None
                            self.last_output = ""

                time.sleep(0.1)  # 减少检查间隔到100毫秒，提高响应性

        except KeyboardInterrupt:
            print("\n\n监控已停止")


def main():
    """主函数"""
    try:
        monitor = MenuInfoMonitor()
        monitor.monitor()
    except Exception as e:
        print(f"错误: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
