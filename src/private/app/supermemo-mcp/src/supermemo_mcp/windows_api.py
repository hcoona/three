"""Windows API 封装模块

提供通用的 Windows API 封装功能，包括：
- 鼠标位置获取
- 窗口操作
- 菜单操作
- 消息发送

支持 Python 3.12+
"""  # noqa: D415, RUF002

import ctypes
import sys
from ctypes import POINTER, Structure, byref, create_unicode_buffer, wintypes
from typing import Any, cast


# Windows 常量定义
class MenuFlags:
    """菜单状态标志位常量"""  # noqa: D415

    MFS_ENABLED = 0
    MFS_CHECKED = 8
    MFS_DEFAULT = 0x1000
    MFS_DISABLED = 2
    MFS_GRAYED = 1
    MFS_HILITE = 0x80


class WindowMessages:
    """Windows 消息常量"""  # noqa: D415

    MN_GETHMENU = 0x01E1


class MenuItemMask:
    """菜单项信息掩码"""  # noqa: D415

    MIIM_STATE = 0x00000001
    MIIM_ID = 0x00000002
    MIIM_SUBMENU = 0x00000004
    MIIM_CHECKMARKS = 0x00000008
    MIIM_TYPE = 0x00000010
    MIIM_DATA = 0x00000020
    MIIM_STRING = 0x00000040
    MIIM_BITMAP = 0x00000080
    MIIM_FTYPE = 0x00000100


class WindowLongIndex:
    """GetWindowLong 索引常量"""  # noqa: D415

    GWL_STYLE = -16
    GWL_EXSTYLE = -20
    GWL_ID = -12
    GWL_USERDATA = -21


class WindowStyles:
    """窗口样式常量"""  # noqa: D415

    WS_VISIBLE = 0x10000000
    WS_CHILD = 0x40000000
    WS_POPUP = 0x80000000
    WS_OVERLAPPED = 0x00000000
    WS_CAPTION = 0x00C00000


class ExtendedWindowStyles:
    """扩展窗口样式常量"""  # noqa: D415

    WS_EX_TOPMOST = 0x00000008
    WS_EX_TOOLWINDOW = 0x00000080
    WS_EX_LAYERED = 0x00080000


class GetWindowCommand:
    """GetWindow 命令常量"""  # noqa: D415

    GW_HWNDFIRST = 0
    GW_HWNDLAST = 1
    GW_HWNDNEXT = 2
    GW_HWNDPREV = 3
    GW_OWNER = 4
    GW_CHILD = 5
    GW_ENABLEDPOPUP = 6


class GetAncestorFlags:
    """GetAncestor 标志常量"""  # noqa: D415

    GA_PARENT = 1
    GA_ROOT = 2
    GA_ROOTOWNER = 3


class ProcessAccess:
    """进程访问权限常量"""  # noqa: D415

    PROCESS_QUERY_INFORMATION = 0x0400
    PROCESS_VM_READ = 0x0010
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


class ChildWindowFromPointFlags:
    """ChildWindowFromPointEx 标志常量"""  # noqa: D415

    CWP_ALL = 0x0000
    CWP_SKIPINVISIBLE = 0x0001
    CWP_SKIPDISABLED = 0x0002
    CWP_SKIPTRANSPARENT = 0x0004


# MENUITEMINFO 结构体定义
class MENUITEMINFO(Structure):
    """Windows MENUITEMINFO 结构体"""  # noqa: D415

    _fields_ = [  # noqa: RUF012
        ("cbSize", wintypes.UINT),
        ("fMask", wintypes.UINT),
        ("fType", wintypes.UINT),
        ("fState", wintypes.UINT),
        ("wID", wintypes.UINT),
        ("hSubMenu", wintypes.HANDLE),
        ("hbmpChecked", wintypes.HBITMAP),
        ("hbmpUnchecked", wintypes.HBITMAP),
        ("dwItemData", ctypes.POINTER(wintypes.ULONG)),
        ("dwTypeData", wintypes.LPWSTR),
        ("cch", wintypes.UINT),
        ("hbmpItem", wintypes.HBITMAP),
    ]


# GUITHREADINFO 结构体定义
class GUITHREADINFO(Structure):
    """Windows GUITHREADINFO 结构体"""  # noqa: D415

    _fields_ = [  # noqa: RUF012
        ("cbSize", wintypes.DWORD),
        ("flags", wintypes.DWORD),
        ("hwndActive", wintypes.HWND),
        ("hwndFocus", wintypes.HWND),
        ("hwndCapture", wintypes.HWND),
        ("hwndMenuOwner", wintypes.HWND),
        ("hwndMoveSize", wintypes.HWND),
        ("hwndCaret", wintypes.HWND),
        ("rcCaret", wintypes.RECT),
    ]


class WindowsAPI:
    """Windows API 封装类"""  # noqa: D415

    def __init__(self) -> None:
        """初始化 Windows API 封装类。

        加载必要的 Windows 库并设置 API 函数原型。
        """  # noqa: D415
        if sys.platform != "win32":
            message = "WindowsAPI is only available on Windows."
            raise OSError(message)

        windll = getattr(ctypes, "windll", None)
        winfunctype = getattr(ctypes, "WINFUNCTYPE", None)
        if windll is None or winfunctype is None:
            message = "Windows ctypes APIs are not available."
            raise OSError(message)

        # 加载必要的 Windows API
        self._windll = cast("Any", windll)
        self._winfunctype = cast("Any", winfunctype)
        self.user32 = self._windll.user32
        self.kernel32 = self._windll.kernel32

        # 设置函数原型
        self._setup_api_prototypes()

    def _setup_api_prototypes(self) -> None:  # noqa: PLR0915
        """设置 Windows API 函数的参数类型和返回值类型。

        这确保了 ctypes 能够正确地调用 Windows API 函数，
        并处理参数传递和返回值转换。
        """  # noqa: D415, RUF002
        # GetCursorPos
        self.user32.GetCursorPos.argtypes = [POINTER(wintypes.POINT)]
        self.user32.GetCursorPos.restype = wintypes.BOOL

        # WindowFromPoint
        self.user32.WindowFromPoint.argtypes = [wintypes.POINT]
        self.user32.WindowFromPoint.restype = wintypes.HWND

        # GetClassName
        self.user32.GetClassNameW.argtypes = [
            wintypes.HWND,
            wintypes.LPWSTR,
            ctypes.c_int,
        ]
        self.user32.GetClassNameW.restype = ctypes.c_int

        # SendMessage
        self.user32.SendMessageW.argtypes = [
            wintypes.HWND,
            wintypes.UINT,
            wintypes.WPARAM,
            wintypes.LPARAM,
        ]
        self.user32.SendMessageW.restype = ctypes.c_long

        # GetMenuItemCount
        self.user32.GetMenuItemCount.argtypes = [wintypes.HANDLE]
        self.user32.GetMenuItemCount.restype = ctypes.c_int

        # GetMenuItemInfo
        self.user32.GetMenuItemInfoW.argtypes = [
            wintypes.HANDLE,
            wintypes.UINT,
            wintypes.BOOL,
            POINTER(MENUITEMINFO),
        ]
        self.user32.GetMenuItemInfoW.restype = wintypes.BOOL

        # GetMenuItemID
        self.user32.GetMenuItemID.argtypes = [wintypes.HANDLE, ctypes.c_int]
        self.user32.GetMenuItemID.restype = wintypes.UINT

        # GetLastError
        self.kernel32.GetLastError.argtypes = []
        self.kernel32.GetLastError.restype = wintypes.DWORD

        # GetParent
        self.user32.GetParent.argtypes = [wintypes.HWND]
        self.user32.GetParent.restype = wintypes.HWND

        # GetWindowText
        self.user32.GetWindowTextW.argtypes = [
            wintypes.HWND,
            wintypes.LPWSTR,
            ctypes.c_int,
        ]
        self.user32.GetWindowTextW.restype = ctypes.c_int

        # GetWindowTextLength
        self.user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        self.user32.GetWindowTextLengthW.restype = ctypes.c_int

        # GetWindowRect
        self.user32.GetWindowRect.argtypes = [
            wintypes.HWND,
            POINTER(wintypes.RECT),
        ]
        self.user32.GetWindowRect.restype = wintypes.BOOL

        # GetClientRect
        self.user32.GetClientRect.argtypes = [
            wintypes.HWND,
            POINTER(wintypes.RECT),
        ]
        self.user32.GetClientRect.restype = wintypes.BOOL

        # IsWindowVisible
        self.user32.IsWindowVisible.argtypes = [wintypes.HWND]
        self.user32.IsWindowVisible.restype = wintypes.BOOL

        # IsWindowEnabled
        self.user32.IsWindowEnabled.argtypes = [wintypes.HWND]
        self.user32.IsWindowEnabled.restype = wintypes.BOOL

        # GetWindowLong
        self.user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
        self.user32.GetWindowLongW.restype = wintypes.LONG

        # GetWindowThreadProcessId
        self.user32.GetWindowThreadProcessId.argtypes = [
            wintypes.HWND,
            POINTER(wintypes.DWORD),
        ]
        self.user32.GetWindowThreadProcessId.restype = wintypes.DWORD

        # GetWindow
        self.user32.GetWindow.argtypes = [wintypes.HWND, wintypes.UINT]
        self.user32.GetWindow.restype = wintypes.HWND

        # GetAncestor
        self.user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
        self.user32.GetAncestor.restype = wintypes.HWND

        # OpenProcess
        self.kernel32.OpenProcess.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        self.kernel32.OpenProcess.restype = wintypes.HANDLE

        # CloseHandle
        self.kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        self.kernel32.CloseHandle.restype = wintypes.BOOL

        # GetModuleBaseName
        self.psapi = self._windll.psapi
        self.psapi.GetModuleBaseNameW.argtypes = [
            wintypes.HANDLE,
            wintypes.HMODULE,
            wintypes.LPWSTR,
            wintypes.DWORD,
        ]
        self.psapi.GetModuleBaseNameW.restype = wintypes.DWORD

        # FindWindow
        self.user32.FindWindowW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR]
        self.user32.FindWindowW.restype = wintypes.HWND

        # EnumWindows callback type
        self.enum_windows_proc_type = self._winfunctype(
            wintypes.BOOL, wintypes.HWND, wintypes.LPARAM
        )

        # EnumWindows
        self.user32.EnumWindows.argtypes = [
            self.enum_windows_proc_type,
            wintypes.LPARAM,
        ]
        self.user32.EnumWindows.restype = wintypes.BOOL

        # GetForegroundWindow
        self.user32.GetForegroundWindow.argtypes = []
        self.user32.GetForegroundWindow.restype = wintypes.HWND

        # GetFocus
        self.user32.GetFocus.argtypes = []
        self.user32.GetFocus.restype = wintypes.HWND

        # GetActiveWindow
        self.user32.GetActiveWindow.argtypes = []
        self.user32.GetActiveWindow.restype = wintypes.HWND

        # ChildWindowFromPoint
        self.user32.ChildWindowFromPoint.argtypes = [
            wintypes.HWND,
            wintypes.POINT,
        ]
        self.user32.ChildWindowFromPoint.restype = wintypes.HWND

        # ChildWindowFromPointEx
        self.user32.ChildWindowFromPointEx.argtypes = [
            wintypes.HWND,
            wintypes.POINT,
            wintypes.UINT,
        ]
        self.user32.ChildWindowFromPointEx.restype = wintypes.HWND

        # RealChildWindowFromPoint
        self.user32.RealChildWindowFromPoint.argtypes = [
            wintypes.HWND,
            wintypes.POINT,
        ]
        self.user32.RealChildWindowFromPoint.restype = wintypes.HWND

        # GetGUIThreadInfo
        self.user32.GetGUIThreadInfo.argtypes = [
            wintypes.DWORD,
            POINTER(GUITHREADINFO),
        ]
        self.user32.GetGUIThreadInfo.restype = wintypes.BOOL

    def get_cursor_pos(self) -> tuple[int, int]:
        """获取当前鼠标在屏幕上的位置。

        Returns:
            tuple[int, int]: 鼠标的 (x, y) 坐标，如果获取失败则返回 (0, 0)
        """  # noqa: D415, RUF002
        point = wintypes.POINT()
        if self.user32.GetCursorPos(byref(point)):
            return point.x, point.y
        return 0, 0

    def get_window_from_point(self, x: int, y: int) -> int | None:
        """根据屏幕坐标获取对应位置的窗口句柄。

        Args:
            x (int): 屏幕 X 坐标
            y (int): 屏幕 Y 坐标

        Returns:
            Optional[int]: 窗口句柄，如果没有找到窗口则返回 None
        """  # noqa: D415, RUF002
        point = wintypes.POINT(x, y)
        hwnd = self.user32.WindowFromPoint(point)
        return hwnd if hwnd else None

    def get_window_class_name(self, hwnd: int) -> str:
        """获取窗口类名。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            str: 窗口类名，如果获取失败则返回空字符串
        """  # noqa: D415, RUF002
        buffer = create_unicode_buffer(256)
        length = self.user32.GetClassNameW(hwnd, buffer, 256)
        return buffer.value if length > 0 else ""

    def is_popup_menu(self, hwnd: int) -> bool:
        """判断是否为标准弹出菜单。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            bool: 如果是标准弹出菜单则返回 True，否则返回 False
        """  # noqa: D415, RUF002
        class_name = self.get_window_class_name(hwnd)
        return class_name == "#32768"

    def get_menu_handle(self, hwnd: int) -> int | None:
        """获取菜单句柄。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            Optional[int]: 菜单句柄，如果获取失败则返回 None
        """  # noqa: D415, RUF002
        try:
            hmenu = self.user32.SendMessageW(
                hwnd, WindowMessages.MN_GETHMENU, 0, 0
            )
            return hmenu if hmenu else None  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def get_menu_item_count(self, hmenu: int) -> int:
        """获取菜单项数量。

        Args:
            hmenu (int): 菜单句柄

        Returns:
            int: 菜单项数量，如果获取失败则返回 0
        """  # noqa: D415, RUF002
        try:
            count = self.user32.GetMenuItemCount(hmenu)
            return max(0, count)
        except Exception:  # noqa: BLE001
            return 0

    def get_menu_item_state(self, hmenu: int, position: int) -> int:
        """获取菜单项状态。

        Args:
            hmenu (int): 菜单句柄
            position (int): 菜单项位置索引

        Returns:
            int: 菜单项状态标志位，如果获取失败则返回 -1
        """  # noqa: D415, RUF002
        try:
            menu_info = MENUITEMINFO()
            menu_info.cbSize = ctypes.sizeof(MENUITEMINFO)
            menu_info.fMask = MenuItemMask.MIIM_STATE

            success = self.user32.GetMenuItemInfoW(
                hmenu,
                position,
                True,  # noqa: FBT003
                byref(menu_info),
            )
            if success:
                return menu_info.fState
        except Exception:  # noqa: BLE001, S110
            pass
        return -1

    def get_menu_item_id(self, hmenu: int, position: int) -> int:
        """获取菜单项 ID。

        Args:
            hmenu (int): 菜单句柄
            position (int): 菜单项位置索引

        Returns:
            int: 菜单项 ID，如果获取失败则返回 -1
        """  # noqa: D415, RUF002
        try:
            item_id = self.user32.GetMenuItemID(hmenu, position)
            return item_id if item_id != 0xFFFFFFFF else -1  # noqa: PLR2004, TRY300
        except Exception:  # noqa: BLE001
            return -1

    def get_menu_item_text(self, hmenu: int, position: int) -> str:  # noqa: C901, PLR0911
        """获取菜单项文本。

        Args:
            hmenu (int): 菜单句柄
            position (int): 菜单项位置索引

        Returns:
            str: 菜单项文本，如果获取失败则返回错误描述或默认文本
        """  # noqa: D415, RUF002
        try:
            # 方法1：使用 GetMenuItemInfoW 获取完整的菜单项信息  # noqa: RUF003
            menu_info = MENUITEMINFO()
            menu_info.cbSize = ctypes.sizeof(MENUITEMINFO)
            menu_info.fMask = (
                MenuItemMask.MIIM_STRING
                | MenuItemMask.MIIM_FTYPE
                | MenuItemMask.MIIM_ID
                | MenuItemMask.MIIM_STATE
            )
            menu_info.dwTypeData = None
            menu_info.cch = 0

            success = self.user32.GetMenuItemInfoW(
                hmenu,
                position,
                True,  # noqa: FBT003
                byref(menu_info),
            )
            if not success:
                # 如果获取失败，尝试使用 GetMenuStringW  # noqa: RUF003
                buffer_size = 256
                buffer = create_unicode_buffer(buffer_size)
                length = self.user32.GetMenuStringW(
                    hmenu,
                    position,
                    buffer,
                    buffer_size,
                    0x400,  # MF_BYPOSITION
                )

                if length > 0 and buffer.value:
                    text = buffer.value.replace("&", "")
                    return text if text.strip() else "{Empty Text}"
                return f"{{Failed: pos={position}}}"

            # 检查菜单项类型
            # MFT_SEPARATOR = 0x800, MFT_OWNERDRAW = 0x100, MFT_BITMAP = 0x004
            if menu_info.fType & 0x800:  # 分隔符
                return "{Separator}"
            if menu_info.fType & 0x100:  # 所有者绘制
                return f"{{Owner-drawn: ID={menu_info.wID}}}"
            if menu_info.fType & 0x004:  # 位图
                return f"{{Bitmap: ID={menu_info.wID}}}"

            # 尝试获取字符串长度
            if menu_info.cch == 0:
                # 重新调用以获取字符串长度
                menu_info.fMask = MenuItemMask.MIIM_STRING
                menu_info.dwTypeData = None
                menu_info.cch = 0

                success = self.user32.GetMenuItemInfoW(
                    hmenu,
                    position,
                    True,  # noqa: FBT003
                    byref(menu_info),
                )

                if not success or menu_info.cch == 0:
                    # 最后尝试使用 GetMenuStringW
                    buffer_size = 256
                    buffer = create_unicode_buffer(buffer_size)
                    length = self.user32.GetMenuStringW(
                        hmenu,
                        position,
                        buffer,
                        buffer_size,
                        0x400,  # MF_BYPOSITION
                    )

                    if length > 0 and buffer.value:
                        text = buffer.value.replace("&", "")
                        return text if text.strip() else "{Empty Text}"
                    return f"{{No text: type={menu_info.fType:X}, id={menu_info.wID}}}"  # noqa: E501

            # 分配缓冲区并获取文本
            buffer_size = menu_info.cch + 2  # 额外的安全缓冲
            buffer = create_unicode_buffer(buffer_size)
            menu_info.dwTypeData = buffer
            menu_info.cch = buffer_size

            success = self.user32.GetMenuItemInfoW(
                hmenu,
                position,
                True,  # noqa: FBT003
                byref(menu_info),
            )
            if success and buffer.value:
                text = buffer.value.replace("&", "")  # 移除热键指示符
                return text if text.strip() else "{Empty Text}"
            return f"{{GetMenuItemInfo failed: pos={position}}}"  # noqa: TRY300

        except Exception as e:  # noqa: BLE001
            return f"{{Error: {str(e)[:30]}}}"

    def get_window_text(self, hwnd: int) -> str:
        """获取窗口标题文本。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            str: 窗口标题文本，如果获取失败则返回空字符串
        """  # noqa: D415, RUF002
        buffer = create_unicode_buffer(512)
        length = self.user32.GetWindowTextW(hwnd, buffer, 512)
        return buffer.value if length > 0 else ""

    def get_window_text_length(self, hwnd: int) -> int:
        """获取窗口标题文本长度。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            int: 窗口标题文本长度，如果获取失败则返回 -1
        """  # noqa: D415, RUF002
        try:
            length = self.user32.GetWindowTextLengthW(hwnd)
            return length if length != 0xFFFFFFFF else -1  # noqa: PLR2004, TRY300
        except Exception:  # noqa: BLE001
            return -1

    def get_window_rect(self, hwnd: int) -> tuple[int, int, int, int] | None:
        """获取窗口的外部矩形（包括边框和标题栏）。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            Optional[tuple[int, int, int, int]]: 窗口外部矩形 (left, top, right, bottom)，
            如果获取失败则返回 None
        """  # noqa: D415, E501, RUF002
        try:
            rect = wintypes.RECT()
            success = self.user32.GetWindowRect(hwnd, byref(rect))
            if success:
                return (rect.left, rect.top, rect.right, rect.bottom)
        except Exception:  # noqa: BLE001, S110
            pass
        return None

    def get_client_rect(self, hwnd: int) -> tuple[int, int, int, int] | None:
        """获取窗口的客户区矩形（不包括边框和标题栏）。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            Optional[tuple[int, int, int, int]]: 窗口客户区矩形 (left, top, right, bottom)，
            如果获取失败则返回 None
        """  # noqa: D415, E501, RUF002
        try:
            rect = wintypes.RECT()
            success = self.user32.GetClientRect(hwnd, byref(rect))
            if success:
                return (rect.left, rect.top, rect.right, rect.bottom)
        except Exception:  # noqa: BLE001, S110
            pass
        return None

    def is_window_visible(self, hwnd: int) -> bool:
        """判断窗口是否可见。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            bool: 如果窗口可见则返回 True，否则返回 False
        """  # noqa: D415, RUF002
        try:
            return bool(self.user32.IsWindowVisible(hwnd))
        except Exception:  # noqa: BLE001
            return False

    def is_window_enabled(self, hwnd: int) -> bool:
        """判断窗口是否可用。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            bool: 如果窗口可用则返回 True，否则返回 False
        """  # noqa: D415, RUF002
        try:
            return bool(self.user32.IsWindowEnabled(hwnd))
        except Exception:  # noqa: BLE001
            return False

    def get_window_long(self, hwnd: int, nIndex: int) -> int:  # noqa: N803
        """获取窗口的扩展信息。

        Args:
            hwnd (int): 窗口句柄
            nIndex (int): 要获取的扩展信息的索引

        Returns:
            int: 窗口的扩展信息，如果获取失败则返回 0
        """  # noqa: D415, RUF002
        try:
            info = self.user32.GetWindowLongW(hwnd, nIndex)
            return info if info != 0xFFFFFFFF else 0  # noqa: PLR2004, TRY300
        except Exception:  # noqa: BLE001
            return 0

    def get_window_thread_process_id(self, hwnd: int) -> tuple[int, int] | None:
        """获取窗口所属的线程和进程 ID。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            Optional[tuple[int, int]]: 线程 ID 和进程 ID 元组，如果获取失败则返回 None
        """  # noqa: D415, E501, RUF002
        try:
            process_id = wintypes.DWORD()
            thread_id = self.user32.GetWindowThreadProcessId(
                hwnd, byref(process_id)
            )
            return (thread_id, process_id.value)  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def get_window_ancestor_chain(self, hwnd: int) -> list[tuple[int, str]]:
        """获取窗口的祖先链信息。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            list[tuple[int, str]]: 祖先链信息列表，每个元素为 (窗口句柄, 窗口标题文本) 元组，
            如果获取失败则返回空列表
        """  # noqa: D415, E501, RUF002
        ancestors = []
        try:
            while hwnd:
                title = self.get_window_text(hwnd)
                ancestors.append((hwnd, title))
                hwnd = self.user32.GetParent(hwnd)
        except Exception:  # noqa: BLE001, S110
            pass
        return ancestors

    def get_window_details(self, hwnd: int) -> dict:
        """获取窗口的详细信息。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            dict: 包含窗口详细信息的字典，如果获取失败则返回空字典
        """  # noqa: D415, RUF002
        try:
            rect = self.get_window_rect(hwnd)
            client_rect = self.get_client_rect(hwnd)
            visible = self.is_window_visible(hwnd)
            enabled = self.is_window_enabled(hwnd)
            thread_process_id = self.get_window_thread_process_id(hwnd)
            ancestors = self.get_window_ancestor_chain(hwnd)

            return {  # noqa: TRY300
                "hwnd": hwnd,
                "rect": rect,
                "client_rect": client_rect,
                "visible": visible,
                "enabled": enabled,
                "thread_process_id": thread_process_id,
                "ancestors": ancestors,
            }
        except Exception:  # noqa: BLE001
            return {}

    def get_window_owner(self, hwnd: int) -> int | None:
        """获取窗口的拥有者窗口。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            int | None: 拥有者窗口句柄，如果没有拥有者则返回 None
        """  # noqa: D415, RUF002
        try:
            owner = self.user32.GetWindow(hwnd, GetWindowCommand.GW_OWNER)
            return owner if owner else None  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def get_root_owner_window(self, hwnd: int) -> int | None:
        """获取窗口的根拥有者窗口。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            int | None: 根拥有者窗口句柄，如果获取失败则返回 None
        """  # noqa: D415, RUF002
        try:
            # 对于菜单窗口，尝试多种方法找到真正的根拥有者  # noqa: RUF003
            if self.is_popup_menu(hwnd):
                # 首先尝试通过进程查找主窗口
                real_owner_info = self.find_real_owner_window(hwnd)
                if real_owner_info:
                    real_owner_hwnd, _, _ = real_owner_info
                    return real_owner_hwnd

            # 对于普通窗口，使用标准方法  # noqa: RUF003
            root_owner = self.user32.GetAncestor(
                hwnd, GetAncestorFlags.GA_ROOTOWNER
            )

            # 如果根拥有者和原窗口相同，尝试获取不同的拥有者  # noqa: RUF003
            if root_owner == hwnd:
                # 尝试 GA_ROOT
                root = self.user32.GetAncestor(hwnd, GetAncestorFlags.GA_ROOT)
                if root and root != hwnd:
                    return root

                # 尝试直接拥有者
                owner = self.get_window_owner(hwnd)
                if owner and owner != hwnd:
                    return owner

                return None

            return root_owner if root_owner else None  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def get_window_style_description(self, hwnd: int) -> str:
        """获取窗口样式的可读描述。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            str: 窗口样式描述字符串
        """  # noqa: D415
        try:
            style = self.get_window_long(hwnd, WindowLongIndex.GWL_STYLE)
            ex_style = self.get_window_long(hwnd, WindowLongIndex.GWL_EXSTYLE)

            style_parts = []

            # 基本样式
            if style & WindowStyles.WS_POPUP:
                style_parts.append("Popup")
            if style & WindowStyles.WS_CHILD:
                style_parts.append("Child")
            if style & WindowStyles.WS_VISIBLE:
                style_parts.append("Visible")
            if style & WindowStyles.WS_CAPTION:
                style_parts.append("Caption")

            # 扩展样式
            if ex_style & ExtendedWindowStyles.WS_EX_TOPMOST:
                style_parts.append("TopMost")
            if ex_style & ExtendedWindowStyles.WS_EX_TOOLWINDOW:
                style_parts.append("ToolWindow")
            if ex_style & ExtendedWindowStyles.WS_EX_LAYERED:
                style_parts.append("Layered")

            return " | ".join(style_parts) if style_parts else "None"
        except Exception:  # noqa: BLE001
            return "Unknown"

    def get_enhanced_window_info(self, hwnd: int) -> dict:
        """获取增强的窗口信息，包括拥有者、样式等。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            dict: 包含增强窗口信息的字典
        """  # noqa: D415, RUF002
        try:
            basic_info = self.get_window_details(hwnd)

            # 获取拥有者窗口信息
            owner_hwnd = self.get_window_owner(hwnd)
            owner_class = ""
            owner_title = ""
            if owner_hwnd:
                owner_class = self.get_window_class_name(owner_hwnd)
                owner_title = self.get_window_text(owner_hwnd)

            # 获取根拥有者窗口信息
            root_owner_hwnd = self.get_root_owner_window(hwnd)
            root_owner_class = ""
            root_owner_title = ""
            if (
                root_owner_hwnd  # noqa: PLR1714
                and root_owner_hwnd != hwnd
                and root_owner_hwnd != owner_hwnd
            ):
                root_owner_class = self.get_window_class_name(root_owner_hwnd)
                root_owner_title = self.get_window_text(root_owner_hwnd)

            # 获取样式描述
            style_description = self.get_window_style_description(hwnd)

            enhanced_info = basic_info.copy()
            enhanced_info.update(
                {
                    "owner_hwnd": owner_hwnd,
                    "owner_class": owner_class,
                    "owner_title": owner_title,
                    "root_owner_hwnd": root_owner_hwnd,
                    "root_owner_class": root_owner_class,
                    "root_owner_title": root_owner_title,
                    "style_description": style_description,
                }
            )

            return enhanced_info  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return {}

    def get_process_name(self, process_id: int) -> str:
        """根据进程ID获取进程名称。

        Args:
            process_id (int): 进程ID

        Returns:
            str: 进程名称，如果获取失败则返回进程ID字符串
        """  # noqa: D415, RUF002
        try:
            # 打开进程句柄
            process_handle = self.kernel32.OpenProcess(
                ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION,
                False,  # noqa: FBT003
                process_id,
            )
            if not process_handle:
                return f"PID:{process_id}"

            # 获取进程模块名称
            buffer = create_unicode_buffer(256)
            size = self.psapi.GetModuleBaseNameW(
                process_handle, None, buffer, 256
            )

            # 关闭句柄
            self.kernel32.CloseHandle(process_handle)

            if size > 0:
                return buffer.value
            return f"PID:{process_id}"  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return f"PID:{process_id}"

    def find_real_owner_window(self, hwnd: int) -> tuple[int, str, str] | None:  # noqa: C901
        """查找菜单窗口的真正拥有者应用程序窗口。

        Args:
            hwnd (int): 菜单窗口句柄

        Returns:
            tuple[int, str, str] | None: (窗口句柄, 类名, 标题) 或 None
        """  # noqa: D415
        try:
            # 获取菜单窗口的进程ID
            thread_process_info = self.get_window_thread_process_id(hwnd)
            if not thread_process_info:
                return None

            _, process_id = thread_process_info

            # 收集同一进程的所有窗口
            windows_in_process = []

            def enum_callback(enum_hwnd, lparam):  # noqa: ANN001, ANN202, ARG001
                try:
                    enum_thread_process_info = (
                        self.get_window_thread_process_id(enum_hwnd)
                    )
                    if enum_thread_process_info:
                        _, enum_process_id = enum_thread_process_info
                        if enum_process_id == process_id:
                            class_name = self.get_window_class_name(enum_hwnd)
                            title = self.get_window_text(enum_hwnd)
                            visible = self.is_window_visible(enum_hwnd)

                            # 跳过系统菜单窗口和不可见窗口
                            if class_name != "#32768" and visible and title:
                                windows_in_process.append(
                                    (enum_hwnd, class_name, title)
                                )
                except Exception:  # noqa: BLE001, S110
                    pass
                return True  # 继续枚举

            # 枚举所有顶层窗口
            callback = self.enum_windows_proc_type(enum_callback)
            self.user32.EnumWindows(callback, 0)

            # 寻找最可能的主窗口
            if windows_in_process:
                # 优先选择有意义类名的窗口（不是通用类名）  # noqa: RUF003
                for hwnd_candidate, class_name, title in windows_in_process:
                    if (
                        class_name not in ["Window", "Dialog", "Frame"]
                        and len(class_name) > 3  # noqa: PLR2004
                        and not class_name.startswith("#")
                    ):
                        return (hwnd_candidate, class_name, title)

                # 如果没找到，返回第一个有标题的窗口  # noqa: RUF003
                return windows_in_process[0]

            return None  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def get_comprehensive_window_info(self, hwnd: int) -> dict:
        """获取全面的窗口信息，包括进程名称和真正的拥有者窗口。

        Args:
            hwnd (int): 窗口句柄

        Returns:
            dict: 包含全面窗口信息的字典
        """  # noqa: D415, RUF002
        try:
            # 获取基础增强信息
            enhanced_info = self.get_enhanced_window_info(hwnd)

            # 获取进程名称
            process_name = ""
            if enhanced_info.get("thread_process_id"):
                _, process_id = enhanced_info["thread_process_id"]
                process_name = self.get_process_name(process_id)

            # 查找真正的拥有者窗口
            real_owner_info = self.find_real_owner_window(hwnd)
            real_owner_hwnd = None
            real_owner_class = ""
            real_owner_title = ""

            if real_owner_info:
                real_owner_hwnd, real_owner_class, real_owner_title = (
                    real_owner_info
                )

            # 如果是菜单窗口，查找菜单来源控件  # noqa: RUF003
            menu_source_info = {}
            if self.is_popup_menu(hwnd):
                menu_source_info = self.find_menu_source_control(hwnd)

            # 合并所有信息
            comprehensive_info = enhanced_info.copy()
            comprehensive_info.update(
                {
                    "process_name": process_name,
                    "real_owner_hwnd": real_owner_hwnd,
                    "real_owner_class": real_owner_class,
                    "real_owner_title": real_owner_title,
                }
            )

            # 添加菜单来源信息
            if menu_source_info:
                comprehensive_info.update(menu_source_info)

            return comprehensive_info  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return {}

    def get_foreground_window(self) -> int | None:
        """获取前台窗口。

        Returns:
            int | None: 前台窗口句柄，如果获取失败则返回 None
        """  # noqa: D415, RUF002
        try:
            hwnd = self.user32.GetForegroundWindow()
            return hwnd if hwnd else None  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def get_thread_gui_info(self, thread_id: int) -> dict:
        """获取线程的GUI信息，包括活动窗口、焦点窗口等。

        Args:
            thread_id (int): 线程ID

        Returns:
            dict: 包含GUI信息的字典
        """  # noqa: D415, RUF002
        try:
            gui_info = GUITHREADINFO()
            gui_info.cbSize = ctypes.sizeof(GUITHREADINFO)

            success = self.user32.GetGUIThreadInfo(thread_id, byref(gui_info))
            if success:
                return {
                    "hwnd_active": gui_info.hwndActive,
                    "hwnd_focus": gui_info.hwndFocus,
                    "hwnd_capture": gui_info.hwndCapture,
                    "hwnd_menu_owner": gui_info.hwndMenuOwner,
                    "hwnd_move_size": gui_info.hwndMoveSize,
                    "hwnd_caret": gui_info.hwndCaret,
                }
            return {}  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return {}

    def find_child_window_at_point(
        self, parent_hwnd: int, point: tuple[int, int]
    ) -> int | None:
        """在指定点查找子窗口。

        Args:
            parent_hwnd (int): 父窗口句柄
            point (tuple[int, int]): 屏幕坐标点 (x, y)

        Returns:
            int | None: 子窗口句柄，如果没有找到则返回 None
        """  # noqa: D415, RUF002
        try:
            # 将屏幕坐标转换为父窗口的客户区坐标
            screen_point = wintypes.POINT(point[0], point[1])
            # ScreenToClient would be precise; keep screen coordinates here.

            # 尝试多种方法查找子窗口
            child = self.user32.ChildWindowFromPointEx(
                parent_hwnd,
                screen_point,
                ChildWindowFromPointFlags.CWP_SKIPINVISIBLE,
            )
            if child and child != parent_hwnd:
                return child

            child = self.user32.RealChildWindowFromPoint(
                parent_hwnd, screen_point
            )
            if child and child != parent_hwnd:
                return child

            return None  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return None

    def find_menu_source_control(self, menu_hwnd: int) -> dict:
        """查找菜单的来源控件信息。

        Args:
            menu_hwnd (int): 菜单窗口句柄

        Returns:
            dict: 包含来源控件信息的字典
        """  # noqa: D415
        try:
            result = {
                "source_control_hwnd": None,
                "source_control_class": "",
                "source_control_text": "",
                "focus_window_hwnd": None,
                "focus_window_class": "",
                "focus_window_text": "",
                "menu_owner_hwnd": None,
                "menu_owner_class": "",
                "menu_owner_text": "",
            }

            # 获取菜单所属的线程ID
            thread_process_info = self.get_window_thread_process_id(menu_hwnd)
            if not thread_process_info:
                return result

            thread_id, process_id = thread_process_info  # noqa: RUF059

            # 获取线程的GUI信息
            gui_info = self.get_thread_gui_info(thread_id)

            # 获取菜单拥有者窗口
            menu_owner_hwnd = gui_info.get("hwnd_menu_owner")
            if menu_owner_hwnd:
                result["menu_owner_hwnd"] = menu_owner_hwnd
                result["menu_owner_class"] = self.get_window_class_name(
                    menu_owner_hwnd
                )
                result["menu_owner_text"] = self.get_window_text(
                    menu_owner_hwnd
                )

            # 获取焦点窗口
            focus_hwnd = gui_info.get("hwnd_focus")
            if focus_hwnd:
                result["focus_window_hwnd"] = focus_hwnd
                result["focus_window_class"] = self.get_window_class_name(
                    focus_hwnd
                )
                result["focus_window_text"] = self.get_window_text(focus_hwnd)

                # A focused non-owner window is likely the source control.
                if focus_hwnd != menu_owner_hwnd:
                    result["source_control_hwnd"] = focus_hwnd
                    result["source_control_class"] = result[
                        "focus_window_class"
                    ]
                    result["source_control_text"] = result["focus_window_text"]

            # 如果没有找到明确的来源控件，尝试通过鼠标位置查找  # noqa: RUF003
            if not result["source_control_hwnd"]:
                # 获取菜单位置，推测右键点击位置  # noqa: RUF003
                menu_rect = self.get_window_rect(menu_hwnd)
                if menu_rect:
                    # Use the nearby context-menu position to find a source.
                    click_x = menu_rect[0] - 10  # 菜单左边一点
                    click_y = menu_rect[1] + 10  # 菜单上面一点

                    # 在主窗口中查找子控件
                    main_window_info = self.find_real_owner_window(menu_hwnd)
                    if main_window_info:
                        main_hwnd, _, _ = main_window_info
                        child_hwnd = self.find_child_window_at_point(
                            main_hwnd, (click_x, click_y)
                        )
                        if child_hwnd:
                            result["source_control_hwnd"] = child_hwnd
                            result["source_control_class"] = (
                                self.get_window_class_name(child_hwnd)
                            )
                            result["source_control_text"] = (
                                self.get_window_text(child_hwnd)
                            )

            return result  # noqa: TRY300
        except Exception:  # noqa: BLE001
            return {
                "source_control_hwnd": None,
                "source_control_class": "",
                "source_control_text": "",
                "focus_window_hwnd": None,
                "focus_window_class": "",
                "focus_window_text": "",
                "menu_owner_hwnd": None,
                "menu_owner_class": "",
                "menu_owner_text": "",
            }
