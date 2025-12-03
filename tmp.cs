@startuml
title GSI Known Rating Flow (Timeline View)
' 設定背景與配色，讓畫面更乾淨
skinparam backgroundColor white
skinparam timeLineStyle box

' 隱藏時間軸上的具體數字，讓畫面不雜亂，只看順序
hide time-axis

' 使用 concise 模式，讓 Y 軸的參與者變成平整的條狀
concise "Player" as P
concise "WDTS" as W
concise "GSI" as G

' == 定義初始狀態 ==
@0
P is Idle
W is Idle
G is Idle

' == 匿名評分階段 (Anonymous Phase) ==
@100
P is "Anonymous Play" #LightBlue
W is "Buffering" #LightBlue
P -> W : Play rating #1 (anon)

@200
P -> W : Play rating #2 (anon)

@300
P -> W : Play rating #3 (anon)

' == 插卡登入 (Clock-in) ==
@400
P is "Clock-in" #LightGreen
W is "Processing" #LightGreen
P -> W : Give Card (Clock-in)

@500
W -> G : Send Empty Open #1
G is "Session Active" #LightGreen

@600
W -> G : Update Rating #1

@700
W -> G : Update Rating #2

@800
W -> G : Update Rating #3

' == 實名遊玩 (Known Play) ==
@900
P is "Known Play" #Gold
W is "Real-time" #Gold
P -> W : Play rating #4
W -> G : Update rating #4

@1100
P -> W : Play rating #5
W -> G : Update rating #5

' == 登出 (Clock-out) ==
@1300
P is Idle
W is Idle
W -> G : Close Rating
G is Idle

' == 加上顏色區塊標示 ==
highlight 100 to 400 #AliceBlue;line:Blue : Anonymous Phase (Local Storage)
highlight 400 to 900 #Honeydew;line:Green : Syncing Past Ratings
highlight 900 to 1300 #LemonChiffon;line:Orange : Live Play

@enduml