# Introduction to SuperMemo

SuperMemo shows 2 major windows:

1. Knowledge Tree
2. Element Window

The knowledge tree is a hierarchical structure that organizes your learning materials, while the element window displays the content of the selected item in the knowledge tree.

The element window allows you to view and edit the content of the selected element, which can include text, images, audio, and other media types. This structure helps users navigate through their learning materials efficiently.

Each element can contain various components such as text, images, and audio, which can be edited and managed within the element window.

Element can have different types:

1. Topic
2. Item

## Example Element Data

You can get element data by sending "Copy Element" command to the SuperMemo application. The data is structured in a specific format that includes details about the element, its components, and their properties.

The text is copied to the clipboard after sending the command. However, the encoding of the text is wrong. You need to recover the text

1. Encode to bytes using windows-1252 encoding.
2. Decode the bytes to UTF-8.

```text
Begin Element #52013
Source=c:\opt\sm18-lazy-package-1.4.0\sm18\systems\all in one
Parent=12926
ParentTitle=ç¾Žå›½åœ°å›¾ï¼ˆè‹±æ–‡ï¼‰
Priority=48.99487
Begin ElementInfo #52013
Title=[Occlusion]: ç¾Žå›½åœ°å›¾ï¼ˆè‹±æ–‡ï¼‰
Type=Item
Status=Memorized
FirstGrade=8
Ordinal=12669.000000
Repetitions=1
Lapses=0
Interval=64
LastRepetition=19.05.25
AFactor=3.920
UFactor=64.000
ForgettingIndex=10
Reference=
SourceArticle=0
End ElementInfo #52013
ElementColor=-16777211
AutoPlay=1
BackgroundImage=
BackgroundFile=
BackgroundStyle=Tile
Scaled=1
ReadPointComponent=0
ReadPointStart=0
ReadPointLength=0
ReadPointScrollTop=0
ComponentNo=4
Begin Component #1
Type=Image
Cors=(4895,0,4895,9802)
DisplayAt=255
Hyperlink=0
ImageName=US map (eng)
ImageFile=c:\opt\sm18-lazy-package-1.4.0\sm18\systems\all in one\elements\1\17\27\14371.jpg
Stretch=2
ClickPlay=0
TestElement=0
Transparent=0
Zoom=[0,0,0,0]
End Component #1
Begin Component #2
Type=Image
Cors=(4895,0,4895,9809)
DisplayAt=63
Hyperlink=0
ImageName=__Occlusion: US map (eng) 511827265
ImageFile=c:\opt\sm18-lazy-package-1.4.0\sm18\systems\all in one\elements\7\4\19\64396.png
Stretch=2
ClickPlay=0
TestElement=0
Transparent=0
Zoom=[0,0,0,0]
End Component #2
Begin Component #3
Type=HTML
Cors=(0,0,4895,4893)
DisplayAt=255
Hyperlink=0
Text=US State Name
TestElement=0
ReadOnly=0
FullHTML=1
Style=0
End Component #3
Begin Component #4
Type=HTML
Cors=(0,4893,4895,4893)
DisplayAt=223
Hyperlink=0
HTMName=Wyoming /waÉªËˆÉ™ÊŠmÉªÅ‹/
HTMFile=c:\opt\sm18-lazy-package-1.4.0\sm18\systems\all in one\elements\7\4\19\64397.HTM
TestElement=0
ReadOnly=0
FullHTML=1
Style=0
End Component #4
Begin RepHist #52013
ElNo=52013 Rep=1 Laps=0 Date=19.05.2025 Hour=14.936 Int=0 Grade=8 Priority=0 expFI=99
End RepHist #52013
End Element #52013
```

In above example, the element with ID 52013 is a topic that contains 2 images and 2 HTML components. The first image is a map of the United States, and the second image is an occlusion of the first image. The HTML components are question & answer pairs related to the state names in the map. If the content is a short plain text, it could (but is not required to) be stored in the `Text` field of the HTML component. If the field `HTMFile` appears, it indicates that the HTML content is stored in a separate file. The ending part of the element data includes repetition history, which tracks the learning progress for this element.

If you paste back the copied element data into SuperMemo, it will create a new element according to the provided structure.
