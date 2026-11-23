"""Pinned archive, weight and binary-wheel lock table for ``copista-28m``.

Every network artifact fetched by ``setup-copista`` is pre-pinned here with an
exact HTTPS URL, byte count, and ``SHA-256`` digest so installation never
invokes online dependency resolution outside the metered ledger.
"""

from __future__ import annotations

import hashlib
import platform
import sys

from sheet_music_omr.harness.stops import StopConditionError

COPISTERIA_COMMIT = "2be4a9d88c15fd6b36fe7c97e0fa816f08804570"
COPISTERIA_ARCHIVE_URL = (
    f"https://codeload.github.com/kobimusic/copisteria/tar.gz/"
    f"{COPISTERIA_COMMIT}"
)
COPISTERIA_ARCHIVE_BYTES = 822_822
COPISTERIA_ARCHIVE_SHA256 = (
    "9632a54a9e6dee0764b885d9777d373ba9fbaf5e307d2ec349de0c3799b72f91"
)

COPISTA_HF_REVISION = "44464d11234a22ba23634ec8eb1e31e3b012021a"
COPISTA_WEIGHTS: tuple[tuple[str, int, str], ...] = (
    (
        "evidence-2m.pt",
        8_145_299,
        "7a3eb0362b44016e941680c5df1e133239576592ab134057c3dbac8e9f3b8838",
    ),
    (
        "small/v7_obj-recall-30m_best.pt",
        115_352_175,
        "9f1db282271fb1064461387da4a2e15de14b099c49b619c21429cdc45be85cfe",
    ),
)

type WheelSpec = tuple[str, str, str, int, str]

_COMMON_PURE_WHEELS: tuple[WheelSpec, ...] = (
    (
        "filelock",
        "4.3.0",
        "https://files.pythonhosted.org/packages/68/66/870b527908a06c7ec0e91239bd2c4d0bb22921efd4f915428b4e47d461a6/filelock-4.3.0-py3-none-any.whl",
        244_553,
        "f933f39f7e01249a4a49b9abab30edc6d4b874624c3de5dd0b0d137aa8adb979",
    ),
    (
        "fsspec",
        "2026.9.0",
        "https://files.pythonhosted.org/packages/6c/c0/a98505f18594f1bce828bb159cec0fcf9860562f1a2c85913409fc8f3d9e/fsspec-2026.9.0-py3-none-any.whl",
        221_738,
        "8dd6e646e99ea382bd85f97a45e6b526a442d79423a7dc673f1e2756d05fcb5f",
    ),
    (
        "jinja2",
        "3.1.6",
        "https://files.pythonhosted.org/packages/62/a1/3d680cbfd5f4b8f15abc1d571870c5fc3e594bb582bc3b64ea099db13e56/jinja2-3.1.6-py3-none-any.whl",
        134_899,
        "85ece4451f492d0c13c5dd7c13a64681a86afae63a5f347908daf103ce6d2f67",
    ),
    (
        "mpmath",
        "1.3.0",
        "https://files.pythonhosted.org/packages/43/e3/7d92a15f894aa0c9c4b49b8ee9ac9850d6e63b03c9c32c0367a13ae62209/mpmath-1.3.0-py3-none-any.whl",
        536_198,
        "a0b2b9fe80bbcd81a6647ff13108738cfb482d481d826cc0e02f5b35e5c88d2c",
    ),
    (
        "networkx",
        "3.7",
        "https://files.pythonhosted.org/packages/7e/cd/fe58041e9011f307c490e3e17dd48cc516448f7c698a3f2d9d9d65d7e6a8/networkx-3.7-py3-none-any.whl",
        2_142_205,
        "e3fd2c13a7814cee3746340d8d7f8598a67f16a58bf47fb7f8793fab6efca1b0",
    ),
    (
        "packaging",
        "26.3",
        "https://files.pythonhosted.org/packages/63/34/ba1c580383c9eada3711951fef0795c80b829a078d72188184bcab9dd527/packaging-26.3-py3-none-any.whl",
        129_956,
        "d7193f7c8e4e93f444fde0262bf90af30e16fa0ad0ad44cb553c87339b23cd1c",
    ),
    (
        "setuptools",
        "84.2.1",
        "https://files.pythonhosted.org/packages/ef/55/6dc6d6bb953bdfc52519a0059592c52b6e3b8ad51cf8256dffceb55b0610/setuptools-84.2.1-py3-none-any.whl",
        876_263,
        "ca140b45d5325a9478f880e351484f62fef15f336462e449ca0c03c873d589c2",
    ),
    (
        "sympy",
        "1.14.0",
        "https://files.pythonhosted.org/packages/a2/09/77d55d46fd61b4a135c444fc97158ef34a095e5681d0a6c10b75bf356191/sympy-1.14.0-py3-none-any.whl",
        6_299_353,
        "e091cc3e99d2141a0ba2847328f5479b05d94a6635cb96148ccb3f34671bd8f5",
    ),
    (
        "typing-extensions",
        "4.16.0",
        "https://files.pythonhosted.org/packages/49/d3/b8441a820a491ddfc024b0b0cf0393375b75ea13866d9c66727e54c2fc80/typing_extensions-4.16.0-py3-none-any.whl",
        45_571,
        "481caa481374e813c1b176ada14e97f1f67a4539ce9cfeb3f350d78d6370c2e8",
    ),
    (
        "wheel",
        "0.48.0",
        "https://files.pythonhosted.org/packages/2e/29/69cfbb602cd91690c55d38ba9fe53e6a7e76a6fa647bf38f19c138d25449/wheel-0.48.0-py3-none-any.whl",
        33_320,
        "3217dcc807155e45db462d7ef2431f5ddda0d7273b700d05a67b271ceb1287ab",
    ),
)

COPISTA_WHEEL_LOCK: dict[str, tuple[WheelSpec, ...]] = {
    "linux_x86_64": (
        *_COMMON_PURE_WHEELS,
        (
            "markupsafe",
            "3.0.4",
            "https://files.pythonhosted.org/packages/8f/db/d7282caf7ab03af44d5d6fdbaa019b35c7d7f1c90588b839c07cba640d6a/markupsafe-3.0.4-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.manylinux_2_28_x86_64.whl",
            23_049,
            "a8e9f292fcda89b324f2f5c91d13f1424a153e40fc2756f38ee23b15835ff300",
        ),
        (
            "numpy",
            "2.5.5",
            "https://files.pythonhosted.org/packages/a4/1f/fcdce1061fba2bfb2e9bfda01fbd8132d5f7522a4f7c8e2b2f629d54ee38/numpy-2.5.5-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
            15_718_051,
            "c48960d420e90cf305cb1fde0d4e6082cc90512cda87a9acf0b6e04e91cf4abe",
        ),
        (
            "opencv-python",
            "5.1.0.95",
            "https://files.pythonhosted.org/packages/69/b5/d55ad2e38fafe028c21639fa750ddcfdee29eaa047c3638e2e71741a6cda/opencv_python-5.1.0.95-cp37-abi3-manylinux2014_x86_64.manylinux_2_17_x86_64.whl",
            72_331_896,
            "63ad2b9e34c8ee4acde30fd93a162bc91f53480273f595667fedb5bb65ce6069",
        ),
        (
            "pillow",
            "13.0.0",
            "https://files.pythonhosted.org/packages/1d/f2/1371707ce438ba9a43f9d24089828494ec6f9396e28cd7903aa704dd6306/pillow-13.0.0-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
            7_142_574,
            "e91c2b1cbeae768c98752e67e717e283d68dae45717b41f2d91eaee648e37b7d",
        ),
        (
            "pypdfium2",
            "5.15.2",
            "https://files.pythonhosted.org/packages/c8/e5/2e668bc1bc6fe22f9006694b5571a2dd2c9b874d6a21d16ee839c8f7f6ea/pypdfium2-5.15.2-py3-none-manylinux_2_17_x86_64.manylinux2014_x86_64.whl",
            4_020_833,
            "dc31aea5b0fb9fefc9b537a65f4113980b2c40798445736a7e6565bc85161e37",
        ),
        (
            "scipy",
            "1.18.1",
            "https://files.pythonhosted.org/packages/6b/89/2a844506d49651e9aa1af6ef95b6bd8031cb1d5a4375edec6155037e04cf/scipy-1.18.1-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
            35_329_183,
            "ac0333bdf38309aa3dcbe7e3fa7ea29e7a2c37c6ea306a757b700ded8e4596ad",
        ),
        (
            "torch",
            "2.15.0+cpu",
            "https://download-r2.pytorch.org/whl/cpu/torch-2.15.0%2Bcpu-cp314-cp314-manylinux_2_28_x86_64.whl",
            209_533_799,
            "f089c94912c955269a20e5fee6b6a371209b610849c9ece26e908a748d87a723",
        ),
        (
            "torchvision",
            "0.29.2+cpu",
            "https://download-r2.pytorch.org/whl/cpu/torchvision-0.29.2%2Bcpu-py3-none-manylinux_2_28_x86_64.whl",
            1_715_515,
            "87631236a47be98ab21301e8cb4fc1cbb9325fe0aa7a022e18d2f894087b68c3",
        ),
    ),
    "win_amd64": (
        *_COMMON_PURE_WHEELS,
        (
            "markupsafe",
            "3.0.4",
            "https://files.pythonhosted.org/packages/80/f0/5792ff768a410f93ee3f84fc19345295ffc352d2c936b424cb37e514714c/markupsafe-3.0.4-cp314-cp314-win_amd64.whl",
            14_501,
            "c1bc67752d5f21013cfe430df4062441714eab79f65a6a05e01505957e9c35fe",
        ),
        (
            "numpy",
            "2.5.5",
            "https://files.pythonhosted.org/packages/74/e2/494516771374b43060c1b778b371cba53d576abd3aee1275eecd73896e3b/numpy-2.5.5-cp314-cp314-win_amd64.whl",
            12_710_543,
            "20145b11776d2f7570cd61351b156a51d3a341822282095baa819d87272bc710",
        ),
        (
            "opencv-python",
            "5.1.0.95",
            "https://files.pythonhosted.org/packages/b2/40/e8c3938d34f22e629a7c599482360b1cadebc22b3c5b25bbbdccded52624/opencv_python-5.1.0.95-cp37-abi3-win_amd64.whl",
            47_965_460,
            "ab505983b1c1e21e89b5c43bf59cccc805b8aa7981f4385a17812efa63ceccb9",
        ),
        (
            "pillow",
            "13.0.0",
            "https://files.pythonhosted.org/packages/50/e2/7ad17747517e4c813dca78257d6bb593466f8231baf1384bc98d92745edb/pillow-13.0.0-cp314-cp314-win_amd64.whl",
            7_281_830,
            "f7cbb7a64e7ec664775195e4f81923bc8a93d9451b963f844e59118f63147a7a",
        ),
        (
            "pypdfium2",
            "5.15.2",
            "https://files.pythonhosted.org/packages/fe/7f/8aa446529fa14523e5c18f89e823afc95526689f76a75efe68a82a10d850/pypdfium2-5.15.2-py3-none-win_amd64.whl",
            4_187_589,
            "74d1bace34fb01c660d0b64344d5a4b7d276942b7c6623c88ab59ea21c5acfbd",
        ),
        (
            "scipy",
            "1.18.1",
            "https://files.pythonhosted.org/packages/52/94/d73da0d28f16c45bb9b0a5691b91610b0275c5ef0eb5e43c87cf2dc1bf31/scipy-1.18.1-cp314-cp314-win_amd64.whl",
            37_362_755,
            "78a0d7c918e74a232394117160e7e3db503377572a45bcef8826e4ab8a35feba",
        ),
        (
            "torch",
            "2.15.0+cpu",
            "https://download-r2.pytorch.org/whl/cpu/torch-2.15.0%2Bcpu-cp314-cp314-win_amd64.whl",
            128_631_133,
            "8c145170f1ad54edd1ef1da3c872a4a8ada202e24894893df4ed2fc75a8322af",
        ),
        (
            "torchvision",
            "0.29.2+cpu",
            "https://download-r2.pytorch.org/whl/cpu/torchvision-0.29.2%2Bcpu-py3-none-win_amd64.whl",
            1_379_232,
            "84cb034b4ad99ccfda755270f6a7fff8a1c6694450e29880b99e4a60274e800b",
        ),
    ),
}


def resolve_host_wheel_lock() -> tuple[WheelSpec, ...]:
    """Return pinned Python 3.14 wheel lock entries for the active host."""
    if sys.version_info[:2] != (3, 14):
        msg = (
            f"copista wheel lock requires Python 3.14, got "
            f"{sys.version_info.major}.{sys.version_info.minor}"
        )
        raise StopConditionError(msg)
    mach = platform.machine().lower()
    if sys.platform.startswith("linux") and mach in {"x86_64", "amd64"}:
        return COPISTA_WHEEL_LOCK["linux_x86_64"]
    if sys.platform == "win32" and mach in {"amd64", "x86_64"}:
        return COPISTA_WHEEL_LOCK["win_amd64"]
    msg = (
        f"unsupported host platform for pinned wheel lock: "
        f"{sys.platform}/{mach}"
    )
    raise StopConditionError(msg)


def compute_wheel_lock_sha256() -> str:
    """Return canonical SHA-256 of the active host's pinned wheel lock."""
    rows = [
        f"{n}:{v}:{sz}:{sha}" for n, v, _u, sz, sha in resolve_host_wheel_lock()
    ]
    return hashlib.sha256("\n".join(rows).encode("utf-8")).hexdigest()
