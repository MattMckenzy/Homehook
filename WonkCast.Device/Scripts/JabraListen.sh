#!/bin/bash
declare -i v=0
 
device='/dev/input/event0'
event_up='*type 1 (EV_KEY), code 115 (KEY_VOLUMEUP), value 1*'
event_down='*type 1 (EV_KEY), code 114 (KEY_VOLUMEDOWN), value 1*'

evtest "$device" | while read line; do
  case $line in
    ($event_up)     LEVEL=$(amixer cget numid=4 | grep -oP ": values=\K[0-9]+" ) && $((LEVEL+1)) && amixer cset numid=4 $LEVEL >/dev/null && echo $LEVEL ;;
    ($event_down)   LEVEL=$(amixer cget numid=4 | grep -oP ": values=\K[0-9]+" ) && $((LEVEL<1 ? LEVEL : LEVEL-1)) && amixer cset numid=4 $LEVEL >/dev/null && echo $LEVEL ;;
  esac
done