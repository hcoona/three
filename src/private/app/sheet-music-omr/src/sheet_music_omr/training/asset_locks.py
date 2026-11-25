"""Pinned wheel, Hugging Face metadata, and TRAIN_DEV aggregate locks for W6."""

from __future__ import annotations

import hashlib
import json
import platform
import sys
from dataclasses import dataclass

from sheet_music_omr.harness.stops import StopConditionError

EXPECTED_TRAINDEV_PORTABLE_SHA256 = (
    "d52fbea06bcf92246dd2625ae47898f85dc9662db413d1eadef213a7ad1f4eb6"
)

TRAINDEV_GOLD_IDS: frozenset[str] = frozenset(
    {
        "gold-07",
        "gold-08",
        "gold-09",
        "gold-10",
        "gold-11",
        "gold-12",
        "gold-13",
        "gold-14",
        "gold-15",
        "gold-16",
        "gold-17",
        "gold-19",
        "gold-20",
        "gold-21",
        "gold-22",
        "gold-23",
    }
)

ALLOWED_WHEEL_LICENSES: frozenset[str] = frozenset(
    {
        "Apache-2.0",
        "MIT",
        "MIT-CMU",
        "BSD-2-Clause",
        "BSD-3-Clause",
        "PSF-2.0",
        "Python-2.0",
        "Apache-2.0 AND Python-2.0",
        "Apache-2.0 OR BSD-2-Clause",
        "ISC",
        "Unlicense",
    }
)

FORBIDDEN_WHEEL_LICENSE_SUBSTRINGS: tuple[str, ...] = (
    "AGPL",
    "GPL",
    "CC-BY-NC",
    "NON-COMMERCIAL",
    "OXR",
)

ALLOWED_MODEL_LICENSES: frozenset[str] = frozenset(
    {"Apache-2.0", "MIT", "BSD-3-Clause"}
)


@dataclass(frozen=True, slots=True)
class LockedWheel:
    """Pinned Python wheel artifact for ``DOWNLOADS/w6-train-venv``."""

    name: str
    version: str
    url: str
    size: int
    sha256: str
    license_id: str


@dataclass(frozen=True, slots=True)
class LockedMetaFile:
    """Pinned non-executable Hugging Face metadata artifact."""

    filename: str
    size: int
    git_oid: str
    sha256: str


@dataclass(frozen=True, slots=True)
class LockedModelRepo:
    """Pinned Hugging Face repository revision and metadata inventory."""

    repo_id: str
    commit_sha: str
    license_id: str
    files: tuple[LockedMetaFile, ...]

    @property
    def slug(self) -> str:
        """Filesystem-safe directory slug for this pinned model."""
        return f"{self.repo_id.replace('/', '--')}@{self.commit_sha[:10]}"


W6_LINUX_X86_64_WHEELS: tuple[LockedWheel, ...] = (
    LockedWheel(
        name="accelerate",
        version="1.16.0",
        url="https://files.pythonhosted.org/packages/29/8e/d77aa769cf76472ede10e73e5c27ed22587ff12091fdd7408bc390f41af7/accelerate-1.16.0-py3-none-any.whl",
        size=389390,
        sha256="180885b6d7b05a19dc2dd6f722c5ce930f615e81e9e5f79d3ca910ddb9e6859e",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="annotated-doc",
        version="0.0.7",
        url="https://files.pythonhosted.org/packages/5e/b1/9530d6bee606979d54501adcc767d958a38929827f11a524b0d93429d2ad/annotated_doc-0.0.7-py3-none-any.whl",
        size=5286,
        sha256="6bc31bbbec6dfaaedc1676dbc11dbda96fe22a2b5c400e9ebad113fec44a9dc6",
        license_id="MIT",
    ),
    LockedWheel(
        name="annotated-types",
        version="0.8.0",
        url="https://files.pythonhosted.org/packages/99/91/8acff4f5e50511b911bbccb72b8628a49c68ce14148cd9f6431094859a90/annotated_types-0.8.0-py3-none-any.whl",
        size=13427,
        sha256="f072f4d804ea359e4eaf198b1af7a8b0943881a87f31bb764f8bf219bb9419e0",
        license_id="MIT",
    ),
    LockedWheel(
        name="anyio",
        version="4.16.0",
        url="https://files.pythonhosted.org/packages/a9/16/7757fb84c819accc7d7e9af5291b261e4abb8627ee040f99e7d35ef2457a/anyio-4.16.0-py3-none-any.whl",
        size=134521,
        sha256="c66ae634e0b8228396b801741beb3294326d4b6102f7bba4041c0dfb58c29026",
        license_id="MIT",
    ),
    LockedWheel(
        name="click",
        version="8.5.1",
        url="https://files.pythonhosted.org/packages/31/7d/164b7ffc8624fe9efb4221bf9af485d5c1e41b9ae118045d2b6dc5457d8b/click-8.5.1-py3-none-any.whl",
        size=126163,
        sha256="8bb5376d2006950386f8c6427613334a2acadc5f559cb80e638d00b4df957e37",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="datasets",
        version="5.2.0",
        url="https://files.pythonhosted.org/packages/ae/22/c92c96de701dd7441f553adaaa480801ff3d37575486ef82df651a45f0ec/datasets-5.2.0-py3-none-any.whl",
        size=608276,
        sha256="4be9e2882caaf5db13eadc39e93f3da2a995187b99acebac1595bf6c58dfa927",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="dill",
        version="0.4.1",
        url="https://files.pythonhosted.org/packages/1e/77/dc8c558f7593132cf8fefec57c4f60c83b16941c574ac5f619abb3ae7933/dill-0.4.1-py3-none-any.whl",
        size=120019,
        sha256="1e1ce33e978ae97fcfcff5638477032b801c46c7c65cf717f95fbc2248f79a9d",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="filelock",
        version="4.3.0",
        url="https://files.pythonhosted.org/packages/68/66/870b527908a06c7ec0e91239bd2c4d0bb22921efd4f915428b4e47d461a6/filelock-4.3.0-py3-none-any.whl",
        size=244553,
        sha256="f933f39f7e01249a4a49b9abab30edc6d4b874624c3de5dd0b0d137aa8adb979",
        license_id="MIT",
    ),
    LockedWheel(
        name="fsspec",
        version="2026.9.0",
        url="https://files.pythonhosted.org/packages/6c/c0/a98505f18594f1bce828bb159cec0fcf9860562f1a2c85913409fc8f3d9e/fsspec-2026.9.0-py3-none-any.whl",
        size=221738,
        sha256="8dd6e646e99ea382bd85f97a45e6b526a442d79423a7dc673f1e2756d05fcb5f",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="h11",
        version="0.16.0",
        url="https://files.pythonhosted.org/packages/04/4b/29cac41a4d98d144bf5f6d33995617b185d14b22401f75ca86f384e87ff1/h11-0.16.0-py3-none-any.whl",
        size=37515,
        sha256="63cf8bbe7522de3bf65932fda1d9c2772064ffb3dae62d55932da54b31cb6c86",
        license_id="MIT",
    ),
    LockedWheel(
        name="hf-xet",
        version="1.8.0",
        url="https://files.pythonhosted.org/packages/f8/1e/1e8ecc05ef034e9a95c18ed32d2ec07905a7cd2ca611a9d88e25ee1f4850/hf_xet-1.8.0-cp38-abi3-manylinux2014_x86_64.manylinux_2_17_x86_64.whl",
        size=4292483,
        sha256="29dab7c904cd1914a0439c6e1306c12c7497c8bd32290fbe563ebf51ca4b6d6d",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="httpcore2",
        version="2.16.2",
        url="https://files.pythonhosted.org/packages/2a/e8/009c347feac27f8960605aa98d55ed263b5870dd4fd80b82fd1d00d5716c/httpcore2-2.16.2-py3-none-any.whl",
        size=85551,
        sha256="16051ae2a42f776007543f32de9349a317b23d2cbb4e1456b4bac0e88b87419a",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="httpx2",
        version="2.16.2",
        url="https://files.pythonhosted.org/packages/77/de/8c4306a3d6aa2495111986addf33a9e561eff4c47552e4c4b9251160d0ad/httpx2-2.16.2-py3-none-any.whl",
        size=97893,
        sha256="f05c13aade4c45dbf1c9060647a1058f0acad62398fad96e741269edb33d46f5",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="huggingface-hub",
        version="2.4.0",
        url="https://files.pythonhosted.org/packages/d4/55/ff47f1b082ecf5f43f4bc1e3b932d68eead171dabbbd1a92666c686301d7/huggingface_hub-2.4.0-py3-none-any.whl",
        size=857021,
        sha256="42ecfd93ede47bdd333daebbf66a32d7bfe2e18752754fe4d2d4f98a0179d801",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="idna",
        version="3.20",
        url="https://files.pythonhosted.org/packages/58/a2/bb081bab032533a855d44de1d56f8e8426114ff1ba5d1f07a438a0a654f8/idna-3.20-py3-none-any.whl",
        size=69583,
        sha256="ab7ae7122974553370f0bdb919e1a960b2cd1bc1ef0276416d896db81c14582c",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="jinja2",
        version="3.1.6",
        url="https://files.pythonhosted.org/packages/62/a1/3d680cbfd5f4b8f15abc1d571870c5fc3e594bb582bc3b64ea099db13e56/jinja2-3.1.6-py3-none-any.whl",
        size=134899,
        sha256="85ece4451f492d0c13c5dd7c13a64681a86afae63a5f347908daf103ce6d2f67",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="markdown-it-py",
        version="4.3.0",
        url="https://files.pythonhosted.org/packages/c9/31/5f0ac2f1a1c67423c995c731a11cd4ad05d081a2be969d74f868bdb9cc70/markdown_it_py-4.3.0-py3-none-any.whl",
        size=96951,
        sha256="7aa1511a46c39138de78d9eba92ce47e63124d8003fc98b3a351909e04239efa",
        license_id="MIT",
    ),
    LockedWheel(
        name="markupsafe",
        version="3.0.4",
        url="https://files.pythonhosted.org/packages/8f/db/d7282caf7ab03af44d5d6fdbaa019b35c7d7f1c90588b839c07cba640d6a/markupsafe-3.0.4-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.manylinux_2_28_x86_64.whl",
        size=23049,
        sha256="a8e9f292fcda89b324f2f5c91d13f1424a153e40fc2756f38ee23b15835ff300",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="mdurl",
        version="0.1.2",
        url="https://files.pythonhosted.org/packages/b3/38/89ba8ad64ae25be8de66a6d463314cf1eb366222074cfda9ee839c56a4b4/mdurl-0.1.2-py3-none-any.whl",
        size=9979,
        sha256="84008a41e51615a49fc9966191ff91509e3c40b939176e643fd50a5c2196b8f8",
        license_id="MIT",
    ),
    LockedWheel(
        name="mpmath",
        version="1.3.0",
        url="https://files.pythonhosted.org/packages/43/e3/7d92a15f894aa0c9c4b49b8ee9ac9850d6e63b03c9c32c0367a13ae62209/mpmath-1.3.0-py3-none-any.whl",
        size=536198,
        sha256="a0b2b9fe80bbcd81a6647ff13108738cfb482d481d826cc0e02f5b35e5c88d2c",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="multiprocess",
        version="0.70.19",
        url="https://files.pythonhosted.org/packages/a0/61/af9115673a5870fd885247e2f1b68c4f1197737da315b520a91c757a861a/multiprocess-0.70.19-py314-none-any.whl",
        size=160318,
        sha256="e8cc7fbdff15c0613f0a1f1f8744bef961b0a164c0ca29bdff53e9d2d93c5e5f",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="networkx",
        version="3.7",
        url="https://files.pythonhosted.org/packages/7e/cd/fe58041e9011f307c490e3e17dd48cc516448f7c698a3f2d9d9d65d7e6a8/networkx-3.7-py3-none-any.whl",
        size=2142205,
        sha256="e3fd2c13a7814cee3746340d8d7f8598a67f16a58bf47fb7f8793fab6efca1b0",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="numpy",
        version="2.5.5",
        url="https://files.pythonhosted.org/packages/a4/1f/fcdce1061fba2bfb2e9bfda01fbd8132d5f7522a4f7c8e2b2f629d54ee38/numpy-2.5.5-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
        size=15718051,
        sha256="c48960d420e90cf305cb1fde0d4e6082cc90512cda87a9acf0b6e04e91cf4abe",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="packaging",
        version="26.3",
        url="https://files.pythonhosted.org/packages/63/34/ba1c580383c9eada3711951fef0795c80b829a078d72188184bcab9dd527/packaging-26.3-py3-none-any.whl",
        size=129956,
        sha256="d7193f7c8e4e93f444fde0262bf90af30e16fa0ad0ad44cb553c87339b23cd1c",
        license_id="Apache-2.0 OR BSD-2-Clause",
    ),
    LockedWheel(
        name="pandas",
        version="3.1.1",
        url="https://files.pythonhosted.org/packages/23/c3/dcf098d687f1f31c97b9c3869809fa2cdb6598039299167f6dfe0cf0c3a5/pandas-3.1.1-cp314-cp314-manylinux_2_24_x86_64.manylinux_2_28_x86_64.whl",
        size=12029794,
        sha256="326459d8a9be11b5f48c7fc1d63e38ba7cfeaebe5d41bd19e4c5f4f11dc9ac91",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="peft",
        version="0.22.0",
        url="https://files.pythonhosted.org/packages/60/f2/107e752b64f5636ff92aed4010bad2448d9dd15d9fcf6c1c01b1075dfd76/peft-0.22.0-py3-none-any.whl",
        size=886740,
        sha256="397f09710e3e1b6ad730211468839aa271732b51a50e99d4f44f2a6e8d9a9f0b",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="pillow",
        version="13.0.0",
        url="https://files.pythonhosted.org/packages/1d/f2/1371707ce438ba9a43f9d24089828494ec6f9396e28cd7903aa704dd6306/pillow-13.0.0-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
        size=7142574,
        sha256="e91c2b1cbeae768c98752e67e717e283d68dae45717b41f2d91eaee648e37b7d",
        license_id="MIT-CMU",
    ),
    LockedWheel(
        name="psutil",
        version="7.2.2",
        url="https://files.pythonhosted.org/packages/b5/70/5d8df3b09e25bce090399cf48e452d25c935ab72dad19406c77f4e828045/psutil-7.2.2-cp36-abi3-manylinux2010_x86_64.manylinux_2_12_x86_64.manylinux_2_28_x86_64.whl",
        size=155560,
        sha256="076a2d2f923fd4821644f5ba89f059523da90dc9014e85f8e45a5774ca5bc6f9",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="pyarrow",
        version="26.0.1",
        url="https://files.pythonhosted.org/packages/f3/57/755a1b54d37c2a9e2c283d8bc648a50aa4ce57f977ce6a4b1d65eba29f51/pyarrow-26.0.1-cp314-cp314-manylinux_2_28_x86_64.whl",
        size=55063542,
        sha256="7ae48ab53610b85e004b03bccff593fbdf22186d0ab48ed42c90797915dac814",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="pydantic",
        version="2.14.4",
        url="https://files.pythonhosted.org/packages/8a/8c/824a686c4ba815ddb0722f9e66272088bccd4e1ae1d2f7015375e4a4fc7f/pydantic-2.14.4-py3-none-any.whl",
        size=494865,
        sha256="653a2ad9f6d8858529c77c72717d0ea991d6c6837fe683246b2f4b2bbfc3fadc",
        license_id="MIT",
    ),
    LockedWheel(
        name="pydantic-core",
        version="2.50.5",
        url="https://files.pythonhosted.org/packages/79/e3/df03dc4e10c460d37dad8921ec9c6f177bc33d4f2da808d5ae4d8f11acfe/pydantic_core-2.50.5-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.whl",
        size=2269323,
        sha256="88b543705849d0c59343f97c38296dbf59eb7b3c9565bd4fe1ed638ba38da5ed",
        license_id="MIT",
    ),
    LockedWheel(
        name="pygments",
        version="2.21.0",
        url="https://files.pythonhosted.org/packages/71/46/17f022dd3e953bf20a04a028a21ec746d942f8d2af30fa0f124fa0e6a684/pygments-2.21.0-py3-none-any.whl",
        size=1250147,
        sha256="2363c69b61c4a97c838da3b130dcd6468f4848992b21a82f2a63ec34377137d9",
        license_id="BSD-2-Clause",
    ),
    LockedWheel(
        name="python-dateutil",
        version="4.0.0",
        url="https://files.pythonhosted.org/packages/2f/a9/52b89563d154433d79b51ed1eb4842bf7334cb2696891a7fe135618853d7/python_dateutil-4.0.0-py3-none-any.whl",
        size=92853,
        sha256="a5e246f79eb80475e691bf93862cead98decd8a498cf53ba0516fcdc0f50dc1c",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="pyyaml",
        version="6.0.4",
        url="https://files.pythonhosted.org/packages/ef/1b/54ce11b40c8fa1883f14eef46525c3c329509dcc268164074fbe60add8ed/pyyaml-6.0.4-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.manylinux_2_28_x86_64.whl",
        size=819491,
        sha256="c604dc2613c417130479cdd6e55c2faa691b6365aa82b3dafddfcecc2eaf1ac7",
        license_id="MIT",
    ),
    LockedWheel(
        name="regex",
        version="2026.11.23",
        url="https://files.pythonhosted.org/packages/73/45/38781bcf04de42515ca6eff3484a693e7d3fd1eefbcb09042360b91a0970/regex-2026.11.23-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.manylinux_2_28_x86_64.whl",
        size=808654,
        sha256="776472af1a00573c46841df3c382b1cdc2bfb79ffcce5713411d1ab9ddcb0dd8",
        license_id="Apache-2.0 AND Python-2.0",
    ),
    LockedWheel(
        name="rich",
        version="15.0.0",
        url="https://files.pythonhosted.org/packages/82/3b/64d4899d73f91ba49a8c18a8ff3f0ea8f1c1d75481760df8c68ef5235bf5/rich-15.0.0-py3-none-any.whl",
        size=310654,
        sha256="33bd4ef74232fb73fe9279a257718407f169c09b78a87ad3d296f548e27de0bb",
        license_id="MIT",
    ),
    LockedWheel(
        name="safetensors",
        version="0.8.0",
        url="https://files.pythonhosted.org/packages/28/50/f203ff3a3ddfe19308efc83c5a3a29ed02bf786732ec35e68bf9162f3365/safetensors-0.8.0-cp310-abi3-manylinux_2_17_x86_64.manylinux2014_x86_64.whl",
        size=516040,
        sha256="fd6f3f93c9a0a7cc2788ee63fb763353d4bd2e89b0751bc78fcf7dda00bea774",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="scipy",
        version="1.18.1",
        url="https://files.pythonhosted.org/packages/6b/89/2a844506d49651e9aa1af6ef95b6bd8031cb1d5a4375edec6155037e04cf/scipy-1.18.1-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
        size=35329183,
        sha256="ac0333bdf38309aa3dcbe7e3fa7ea29e7a2c37c6ea306a757b700ded8e4596ad",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="sentencepiece",
        version="0.2.3",
        url="https://files.pythonhosted.org/packages/ad/b3/4269e1c4494b9a17ed6d11a072b3ae52786eac9084bb5c18cdfa9434b5a5/sentencepiece-0.2.3-cp314-cp314-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl",
        size=1716877,
        sha256="22dce81a65b8a1819eef9fef4970d6581ea0cbde7f58fbbbb42521535eba15e2",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="setuptools",
        version="84.2.1",
        url="https://files.pythonhosted.org/packages/ef/55/6dc6d6bb953bdfc52519a0059592c52b6e3b8ad51cf8256dffceb55b0610/setuptools-84.2.1-py3-none-any.whl",
        size=876263,
        sha256="ca140b45d5325a9478f880e351484f62fef15f336462e449ca0c03c873d589c2",
        license_id="MIT",
    ),
    LockedWheel(
        name="shellingham",
        version="1.5.4",
        url="https://files.pythonhosted.org/packages/e0/f9/0595336914c5619e5f28a1fb793285925a8cd4b432c9da0a987836c7f822/shellingham-1.5.4-py2.py3-none-any.whl",
        size=9755,
        sha256="7ecfff8f2fd72616f7481040475a65b2bf8af90a56c89140852d1120324e8686",
        license_id="ISC",
    ),
    LockedWheel(
        name="sympy",
        version="1.14.0",
        url="https://files.pythonhosted.org/packages/a2/09/77d55d46fd61b4a135c444fc97158ef34a095e5681d0a6c10b75bf356191/sympy-1.14.0-py3-none-any.whl",
        size=6299353,
        sha256="e091cc3e99d2141a0ba2847328f5479b05d94a6635cb96148ccb3f34671bd8f5",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="timm",
        version="1.0.31",
        url="https://files.pythonhosted.org/packages/aa/bb/b909b8f0517a8fa00ffb3a9a0ddb20da9dbe3554eb68dfc3c9a2db99db76/timm-1.0.31-py3-none-any.whl",
        size=2798714,
        sha256="5bd50f7d0fe6d09bf9285ac2523234a1b7e6bf075c0be3e7aed09139fcb617c7",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="tokenizers",
        version="0.23.3",
        url="https://files.pythonhosted.org/packages/3a/40/6706b82693715581457c6d5423eaa7faae576bb0526c5738a57085eb4449/tokenizers-0.23.3-cp310-abi3-manylinux_2_17_x86_64.manylinux2014_x86_64.whl",
        size=3396193,
        sha256="376851d22bcf9d650a5c3090bb83e6cf9e895fbf0595369fa4cd43c1f69b5f87",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="torch",
        version="2.15.0+cpu",
        url="https://download-r2.pytorch.org/whl/cpu/torch-2.15.0%2Bcpu-cp314-cp314-manylinux_2_28_x86_64.whl",
        size=209533799,
        sha256="f089c94912c955269a20e5fee6b6a371209b610849c9ece26e908a748d87a723",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="torchvision",
        version="0.29.2+cpu",
        url="https://download-r2.pytorch.org/whl/cpu/torchvision-0.29.2%2Bcpu-py3-none-manylinux_2_28_x86_64.whl",
        size=1715515,
        sha256="87631236a47be98ab21301e8cb4fc1cbb9325fe0aa7a022e18d2f894087b68c3",
        license_id="BSD-3-Clause",
    ),
    LockedWheel(
        name="tqdm",
        version="4.70.1",
        url="https://files.pythonhosted.org/packages/a7/03/921a3d3c75785aca9ebfbfcabfbc3a1be12e2ab5265deb026d55a5a3f83e/tqdm-4.70.1-py3-none-any.whl",
        size=80199,
        sha256="c293e525e6fef9c20e8728fd4612df02a0aa31bb5fe91ecd93e123b1b7bffa73",
        license_id="MPL-2.0 AND MIT",
    ),
    LockedWheel(
        name="transformers",
        version="5.24.1",
        url="https://files.pythonhosted.org/packages/1c/45/8aa25aeec09086bfd5fa094b91917ff52025c55907363fc94022eac4a0df/transformers-5.24.1-py3-none-any.whl",
        size=13556588,
        sha256="505b39d3e45a3715f3acb81b858eb245ce1a462373b7f99e26f0dcd41ed4cd0f",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="truststore",
        version="0.10.5",
        url="https://files.pythonhosted.org/packages/51/e9/3a7820be2bb0fe53b6bc9c3be26d3d1158004e4c3ab953aa6840b955b1e9/truststore-0.10.5-py3-none-any.whl",
        size=19017,
        sha256="9aaaedaefaf06d8b206278cf8b5012bc897f485a874503501e12d776df78951c",
        license_id="MIT",
    ),
    LockedWheel(
        name="typer",
        version="0.29.2",
        url="https://files.pythonhosted.org/packages/62/f3/864dcb0e165b52e9f75aa3c7e0aeeb9876f139c66eb14d87856bb173b0fa/typer-0.29.2-py3-none-any.whl",
        size=121041,
        sha256="f30731f8ca758368b836b3714872e58e395cef8fcaff918c853b1969ed5274e7",
        license_id="MIT",
    ),
    LockedWheel(
        name="typing-extensions",
        version="4.16.0",
        url="https://files.pythonhosted.org/packages/49/d3/b8441a820a491ddfc024b0b0cf0393375b75ea13866d9c66727e54c2fc80/typing_extensions-4.16.0-py3-none-any.whl",
        size=45571,
        sha256="481caa481374e813c1b176ada14e97f1f67a4539ce9cfeb3f350d78d6370c2e8",
        license_id="PSF-2.0",
    ),
    LockedWheel(
        name="typing-inspection",
        version="0.4.5",
        url="https://files.pythonhosted.org/packages/de/0c/ea2544850a7c135e15033a5c7a8e7211136184b7ae63e2239d557c2b3019/typing_inspection-0.4.5-py3-none-any.whl",
        size=14874,
        sha256="bc210535442b39e1bf2805e0c7a17dfd6a4ace760c84c7f7d16245d23a6b1c39",
        license_id="MIT",
    ),
    LockedWheel(
        name="tzdata",
        version="2026.6",
        url="https://files.pythonhosted.org/packages/0a/66/9a06c2227fedfd4919f0eaaa8b94377d9730882a7e4f95823c9a331ab5e1/tzdata-2026.6-py2.py3-none-any.whl",
        size=348210,
        sha256="0a10eec6e13ef16b627fabed9a5f2ba61a62b1c67f327305f4e7e412a70b4aec",
        license_id="Apache-2.0",
    ),
    LockedWheel(
        name="xxhash",
        version="4.1.0",
        url="https://files.pythonhosted.org/packages/ed/2b/bcf98949b14d64af7720f15b05f24d428e221f1ba43b9023b12eaf0e38be/xxhash-4.1.0-cp314-cp314-manylinux2014_x86_64.manylinux_2_17_x86_64.manylinux_2_28_x86_64.whl",
        size=478028,
        sha256="947868e22608b7bdee242532e707e63d22abf70d05d158640ca2ab94596d63ec",
        license_id="BSD-2-Clause",
    ),
)

W6_MODEL_METADATA_LOCKS: tuple[LockedModelRepo, ...] = (
    LockedModelRepo(
        repo_id="Qwen/Qwen3-VL-2B-Instruct",
        commit_sha="89644892e4d85e24eaac8bacfd4f463576704203",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="README.md",
                size=7136,
                git_oid="13afcb1d2c695b57ebe4ee740db48671069566ce",
                sha256="5fc5be1ca9a3910399bd6239ee5086ab5d82a2a59c5d2b00e887a8835cc110e4",
            ),
            LockedMetaFile(
                filename="chat_template.json",
                size=5502,
                git_oid="e49e75bdda104c8de941eb20e0b3765184fb27bc",
                sha256="6f8a6a55027e3da5160105556cda5dd69f6423f1c32645f6730d32de7773d0c4",
            ),
            LockedMetaFile(
                filename="config.json",
                size=1505,
                git_oid="0cd8c646eb55794594b14fefcd1d3acea5cc000f",
                sha256="bec4b3d446efa05807365c9e1cec03ac590836879d02f3a6da879971154bdd3b",
            ),
            LockedMetaFile(
                filename="generation_config.json",
                size=269,
                git_oid="7bb37933d2d86e4ceaabcc6540be989658f238e9",
                sha256="1e241830b48b397cb0900101421df5450baddc7adf01e5fc86b5615865f3bae4",
            ),
            LockedMetaFile(
                filename="merges.txt",
                size=1671839,
                git_oid="20024bfe7c83998e9aeaf98a0cd6a2ce6306c2f0",
                sha256="599bab54075088774b1733fde865d5bd747cbcc7a547c5bc12610e874e26f5e3",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=390,
                git_oid="2ea84a437d448ff71b08df68fdd949d5cc4ebb64",
                sha256="27225450ac9c6529872ee1924fcb0962ff5634834f817040f444118116f4e516",
            ),
            LockedMetaFile(
                filename="tokenizer.json",
                size=7032403,
                git_oid="c6cc1014128b19d1fc46b1d30a23e3b1d35db421",
                sha256="a5d85b6dcc535e6b93115a9ef287e6132fdbf30270da6218194ba742261173c7",
            ),
            LockedMetaFile(
                filename="tokenizer_config.json",
                size=10868,
                git_oid="d3d3763207692c78780f4bf42d4dadf49a5c8012",
                sha256="c2da771801886ad9ae98181793ffd3dfb7f1af30f6f7c6a4e15d7dbba52e2399",
            ),
            LockedMetaFile(
                filename="video_preprocessor_config.json",
                size=385,
                git_oid="3ba673a5ad7d4d13f54155ecd38b2a94a6dac8fe",
                sha256="7768af27c1fafa9cc9011c1dc20067e03f8915e03b63504550e11d5066986d13",
            ),
            LockedMetaFile(
                filename="vocab.json",
                size=2776833,
                git_oid="4783fe10ac3adce15ac8f358ef5462739852c569",
                sha256="ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="Qwen/Qwen3-VL-4B-Instruct",
        commit_sha="ebb281ec70b05090aa6165b016eac8ec08e71b17",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="README.md",
                size=7133,
                git_oid="542417095b66d3fc13be22bbf6722928787b8ef4",
                sha256="a884e5e78f7d6f7bfe237f909dbc41a126542e259dc79d8ab33cc8980580ff79",
            ),
            LockedMetaFile(
                filename="chat_template.json",
                size=5502,
                git_oid="e49e75bdda104c8de941eb20e0b3765184fb27bc",
                sha256="6f8a6a55027e3da5160105556cda5dd69f6423f1c32645f6730d32de7773d0c4",
            ),
            LockedMetaFile(
                filename="config.json",
                size=1505,
                git_oid="22be8bb79043d39840ca5e70d7a2dab80311602c",
                sha256="edac7703329133edfc53e46ac0081835144c99d7eebf28b71c732694d435224d",
            ),
            LockedMetaFile(
                filename="generation_config.json",
                size=269,
                git_oid="e116347d7ef42621c7a5957f82c015c38d939c1f",
                sha256="8469742d1fce0de951c8909b26a2c0c0d8490837ce476efb114da9e0cefc4d44",
            ),
            LockedMetaFile(
                filename="merges.txt",
                size=1671839,
                git_oid="20024bfe7c83998e9aeaf98a0cd6a2ce6306c2f0",
                sha256="599bab54075088774b1733fde865d5bd747cbcc7a547c5bc12610e874e26f5e3",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=390,
                git_oid="2ea84a437d448ff71b08df68fdd949d5cc4ebb64",
                sha256="27225450ac9c6529872ee1924fcb0962ff5634834f817040f444118116f4e516",
            ),
            LockedMetaFile(
                filename="tokenizer.json",
                size=7032403,
                git_oid="c6cc1014128b19d1fc46b1d30a23e3b1d35db421",
                sha256="a5d85b6dcc535e6b93115a9ef287e6132fdbf30270da6218194ba742261173c7",
            ),
            LockedMetaFile(
                filename="tokenizer_config.json",
                size=10868,
                git_oid="d3d3763207692c78780f4bf42d4dadf49a5c8012",
                sha256="c2da771801886ad9ae98181793ffd3dfb7f1af30f6f7c6a4e15d7dbba52e2399",
            ),
            LockedMetaFile(
                filename="video_preprocessor_config.json",
                size=385,
                git_oid="3ba673a5ad7d4d13f54155ecd38b2a94a6dac8fe",
                sha256="7768af27c1fafa9cc9011c1dc20067e03f8915e03b63504550e11d5066986d13",
            ),
            LockedMetaFile(
                filename="vocab.json",
                size=2776833,
                git_oid="4783fe10ac3adce15ac8f358ef5462739852c569",
                sha256="ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="Qwen/Qwen3.5-2B",
        commit_sha="15852e8c16360a2fea060d615a32b45270f8a8fc",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="LICENSE",
                size=11544,
                git_oid="f938136e3adacfd92be087f6e113b5d6d97f678f",
                sha256="bbedc3fda3305820b977265f01b8619d87570a6739de3a5582c3464840f1e57a",
            ),
            LockedMetaFile(
                filename="README.md",
                size=62814,
                git_oid="2efa49d346c547ea81cd3d1310f414df25934f45",
                sha256="c0e83a849c776e6fa843d011f023132d942a0f0140d903205bf1c363adad2275",
            ),
            LockedMetaFile(
                filename="chat_template.jinja",
                size=7755,
                git_oid="0ef09f214eaa6d9bca297988afc1454b5827b2c7",
                sha256="273d8e0e683b885071fb17e08d71e5f2a5ddfb5309756181681de4f5a1822d80",
            ),
            LockedMetaFile(
                filename="config.json",
                size=2908,
                git_oid="d30a15be1bbfd610f58ad80f1fbeb6778f58a80f",
                sha256="ed1c1723241f23f7f4e23430759cbd7dcfb4103cbdfe052bfe7626b57c2615b4",
            ),
            LockedMetaFile(
                filename="merges.txt",
                size=3353259,
                git_oid="a494e019ca1502219fd0128658b979e5f05ae8e8",
                sha256="a9d356d7bdf1ef4949e3e748e95b8e10ad9d4e2e838eddc38a0a7b6b94d1db8d",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=390,
                git_oid="2ea84a437d448ff71b08df68fdd949d5cc4ebb64",
                sha256="27225450ac9c6529872ee1924fcb0962ff5634834f817040f444118116f4e516",
            ),
            LockedMetaFile(
                filename="tokenizer.json",
                size=12807982,
                git_oid="a73a846725794819aa6e1c9e97d8dc9671c2006d",
                sha256="5f9e4d4901a92b997e463c1f46055088b6cca5ca61a6522d1b9f64c4bb81cb42",
            ),
            LockedMetaFile(
                filename="tokenizer_config.json",
                size=16709,
                git_oid="fae3ce993e07c092ad024dde45e592379fde91bb",
                sha256="49e2b6e395f959f077f1e992b338919c0d4a9732fc6e613995e06557f843500c",
            ),
            LockedMetaFile(
                filename="video_preprocessor_config.json",
                size=385,
                git_oid="3ba673a5ad7d4d13f54155ecd38b2a94a6dac8fe",
                sha256="7768af27c1fafa9cc9011c1dc20067e03f8915e03b63504550e11d5066986d13",
            ),
            LockedMetaFile(
                filename="vocab.json",
                size=6722759,
                git_oid="0aa0ce0658d60ac4a5d609f4eadb0e8e43514176",
                sha256="ce99b4cb2983d118806ce0a8b777a35b093e2000a503ebde25853284c9dfa003",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="PaddlePaddle/PaddleOCR-VL-1.6",
        commit_sha="132c45f52c0a26ed5a6b2f6bd8e3985794cc27dd",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="LICENSE",
                size=11376,
                git_oid="0491a00e80b424eb709078b57e35d8b83ffee985",
                sha256="b8c4d7deccd236af023af1c88c4d4e8f0fc2f41914e0fb23a3ec9678fb5a8456",
            ),
            LockedMetaFile(
                filename="README.md",
                size=21424,
                git_oid="58e9801f72ad5f992f0fc2e397ccf0fb014e5e5f",
                sha256="d6c5b77a660577f6e18021defccd835bdc93a8c6538f5a479d597f3e94288abf",
            ),
            LockedMetaFile(
                filename="added_tokens.json",
                size=25381,
                git_oid="6a2790e1462ecb007f9b92dfb00f594701462889",
                sha256="f59f889088e0fe21c523e7cf121bb6dca3b0bb148cb7159fbb4572c74dfc5644",
            ),
            LockedMetaFile(
                filename="chat_template.jinja",
                size=1474,
                git_oid="d8b8c271d7a7245d4937098f53aa372cff3dba70",
                sha256="2f27812dab7f333e471884e0c803d807f11953d5453140dfb1aaba234f872bc8",
            ),
            LockedMetaFile(
                filename="config.json",
                size=2059,
                git_oid="c54711466ae75457f8e57b909a49dae570bfa5c5",
                sha256="ce7f4565f8b1db78532ad5d1b9ebe55c2139d49bd4cb04778b580a08a598f171",
            ),
            LockedMetaFile(
                filename="generation_config.json",
                size=133,
                git_oid="ba87d71b9881c1d52f0be4286fb63e381ce3febf",
                sha256="a6701d78ab3b4d972307cdec3b69d4c13f46e0d5140514f50ab7d84259324b94",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=641,
                git_oid="d873cd63d04d9e6243999759fc30f7752dab3222",
                sha256="111872ab1e8bb7fd040ac5087bfced7ab8f011f02139b088cba294964c3b1d0e",
            ),
            LockedMetaFile(
                filename="processor_config.json",
                size=137,
                git_oid="033053ac4d8b5de2e47884ce85a6b4939cc58e87",
                sha256="1568858960a9760c54431dae693a6152e601ff55cdf6d2eab97a4a99958faea0",
            ),
            LockedMetaFile(
                filename="special_tokens_map.json",
                size=1151,
                git_oid="dcd70aaa8c9987899d7593995546d9c0cfc6a1f3",
                sha256="d3a125c03103deb2acaf7730791bdbbf196f620e5a2213b664511ff9b4b25bab",
            ),
            LockedMetaFile(
                filename="tokenizer.json",
                size=11189060,
                git_oid="e42528d5cae447340ed0a3e403001c88fe9b0f17",
                sha256="c8a215a59183d0d0781adc33bacd3ce6162716f7fd568fb30234a74d69803a7d",
            ),
            LockedMetaFile(
                filename="tokenizer.model",
                size=1614363,
                git_oid="81e33aa7fdb8ef76fceb1a69f1997021f3056770",
                sha256="34ef7db83df785924fb83d7b887b6e822a031c56e15cff40aaf9b982988180df",
            ),
            LockedMetaFile(
                filename="tokenizer_config.json",
                size=186947,
                git_oid="cd9eb7da01a3209f4fb9b3561e04be022cd87764",
                sha256="1f979337347cc0cb72a6282d8a23ed183539aa81a87a906f022aee2bab83c7c5",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="timm/convnext_tiny.fb_in22k_ft_in1k",
        commit_sha="bc48a87f119bae1b06418f5340bfa97ef75d4609",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="README.md",
                size=15750,
                git_oid="a8de080ddfbe21039dfbb0c384b0f53fafd00f9b",
                sha256="5829109fcf28ec11af3f28fe4477a8319a4aaf39aa67a3ab7b4499e8d1720d4d",
            ),
            LockedMetaFile(
                filename="config.json",
                size=666,
                git_oid="8f645ab74da51c27a2963fd83b2d1f5833ba689e",
                sha256="05b31a3e4e2f16a1ad61f64ea4b34ba2e17fb10e72c48490de088b92b97a6151",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="zenosai/MonkeyOCRv2-S",
        commit_sha="8efa4f856af5d38030d83565c8a79b36e5454ce7",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="README.md",
                size=50510,
                git_oid="a79bfa608ce427c281b476ce80215f7ebc57dd7d",
                sha256="8ec55b1a8e1f23a5ab2b82fba5a0c9947fc225a637ece1ebc1525443c66c004c",
            ),
            LockedMetaFile(
                filename="config.json",
                size=773,
                git_oid="d8bea2cd51c1e3a8cb64e17723bcde615cd63256",
                sha256="bee0068d455828ccc6ae241dd674fec5ad125ce36ce09017c162fb482ddcac73",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=452,
                git_oid="e208c19cf29a5c84f1b98f0e4f23be9c28fe7317",
                sha256="4964d4b23cdd80014d61fb9531f69ea48e0330ea77185fe31c997482fb1ef78b",
            ),
            LockedMetaFile(
                filename="processor_config.json",
                size=142,
                git_oid="fedf9e1759aee7d24660523876de71ba5f97b2e0",
                sha256="9f6af6d666fc55f441cd274d13034dc6b862fef27c5c3a37f56d198c9af1e253",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="ustc-community/dfine-small-coco",
        commit_sha="f79e65b5fbb33ceb9d3ebba042955d7410c608f8",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="README.md",
                size=2811,
                git_oid="a7b47d5e4eee913a8dea5e795b48ae4026819f23",
                sha256="b776f99f32e6419c5cecc4578b2ae176f59bdef5c72c10a89340530ac82a4f32",
            ),
            LockedMetaFile(
                filename="config.json",
                size=6865,
                git_oid="8c117d9be9e70d00d81fb871cd0d24c5e2ac27fa",
                sha256="9f206c4c935c67c957af2f15c3333b4ff224e72b1881bc7bbde9f873edf4448e",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=470,
                git_oid="ea1fbb1edd242104e70301472d089735c68c5f21",
                sha256="7e9e2245806aa3ef068a5321f5eebee3116fa4cec560173231954ffaca930eca",
            ),
        ),
    ),
    LockedModelRepo(
        repo_id="PekingU/rtdetr_v2_r18vd",
        commit_sha="5650961749fa93567c0d46fc7f43ea4f9e914107",
        license_id="Apache-2.0",
        files=(
            LockedMetaFile(
                filename="README.md",
                size=3542,
                git_oid="37d358fb4e1d91881c4081fc8df0b75d0c2cd99e",
                sha256="4a26a3a6de73500f181e4edbc75b3f3f19ca6dde0fdaceeeb78167f223973305",
            ),
            LockedMetaFile(
                filename="config.json",
                size=5437,
                git_oid="6f984c30c65857419deeeebfdf7dc4e7d7bce6f8",
                sha256="ed051ec77cb41c5d9d5e3af21a979b1e890599dfd68434d7636ff82ded4c1527",
            ),
            LockedMetaFile(
                filename="preprocessor_config.json",
                size=444,
                git_oid="fcdff16b42e5ebc51d28f59184490c6eff91a88b",
                sha256="cd38cd59999e7a95d68e487fbe5132df3d4e5c32a0836add57e6126ba0c4eaf1",
            ),
        ),
    ),
)


def verify_lock_licenses() -> None:
    """Fail closed if any locked wheel or model violates W6 license rules."""
    for wheel in W6_LINUX_X86_64_WHEELS:
        upper = wheel.license_id.upper()
        for bad in FORBIDDEN_WHEEL_LICENSE_SUBSTRINGS:
            if bad in upper:
                msg = f"forbidden license {wheel.license_id!r} on {wheel.name}"
                raise StopConditionError(msg)
        allowed_tqdm = (
            wheel.name == "tqdm" and wheel.license_id == "MPL-2.0 AND MIT"
        )
        if not allowed_tqdm and wheel.license_id not in ALLOWED_WHEEL_LICENSES:
            msg = (
                f"unpermitted wheel license {wheel.license_id!r}"
                f" on {wheel.name}"
            )
            raise StopConditionError(msg)
    for repo in W6_MODEL_METADATA_LOCKS:
        if repo.license_id not in ALLOWED_MODEL_LICENSES:
            msg = (
                f"unpermitted model license {repo.license_id!r}"
                f" on {repo.repo_id}"
            )
            raise StopConditionError(msg)
        for mf in repo.files:
            if mf.filename.endswith(
                (".py", ".safetensors", ".bin", ".pt", ".pth")
            ):
                msg = (
                    f"forbidden executable/weight file {mf.filename!r}"
                    " in metadata lock"
                )
                raise StopConditionError(msg)


def resolve_host_w6_wheels() -> tuple[LockedWheel, ...]:
    """Return the locked wheel table for the active Python 3.14 Linux host."""
    verify_lock_licenses()
    if sys.version_info[:2] != (3, 14):
        msg = f"W6 wheel lock requires Python 3.14, got {sys.version_info[:2]}"
        raise StopConditionError(msg)
    mach = platform.machine().lower()
    if sys.platform.startswith("linux") and mach in {"x86_64", "amd64"}:
        return W6_LINUX_X86_64_WHEELS
    msg = f"unsupported platform for W6 wheel lock: {sys.platform}/{mach}"
    raise StopConditionError(msg)


def compute_w6_wheel_lock_sha256(wheels: tuple[LockedWheel, ...]) -> str:
    """Deterministic digest over the resolved wheel lock table."""
    payload = [
        {
            "name": w.name,
            "version": w.version,
            "size": w.size,
            "sha256": w.sha256,
        }
        for w in wheels
    ]
    raw = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode(
        "utf-8"
    )
    return hashlib.sha256(raw).hexdigest()


def compute_w6_metadata_lock_sha256() -> str:
    """Deterministic digest over all pinned Hugging Face metadata files."""
    payload = [
        {
            "repo_id": r.repo_id,
            "commit_sha": r.commit_sha,
            "license_id": r.license_id,
            "files": [
                {"filename": f.filename, "size": f.size, "sha256": f.sha256}
                for f in r.files
            ],
        }
        for r in W6_MODEL_METADATA_LOCKS
    ]
    raw = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode(
        "utf-8"
    )
    return hashlib.sha256(raw).hexdigest()
