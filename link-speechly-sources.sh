#!/bin/bash

# See end of file for calls to linkSource

linkSource () {

  HARDLINK_SOURCE=$1
  HARDLINK_DEST=$2

  if [ ! -d "$HARDLINK_SOURCE" ]; then
    echo "ERROR: Source folder '$HARDLINK_SOURCE' does not exist"
    exit 1
  fi

  pushd $HARDLINK_SOURCE
  ABS_HARDLINK_SOURCE=`pwd`
  popd

  if [ -e "$HARDLINK_DEST" ]; then
    echo
    echo ============ LOCAL CHANGES IN ============

    rsync -au --out-format="%n" --dry-run --exclude "*.meta" $HARDLINK_DEST/* $ABS_HARDLINK_SOURCE

    echo ==========================================
    echo LOCAL: $HARDLINK_DEST
    echo
    read -p "Do you wish to backport? [y/n] " yn
    if echo "$yn" | grep -iq "^y" ;then
      rsync -au -vv --exclude "*.meta" $HARDLINK_DEST/* $ABS_HARDLINK_SOURCE | grep "is newer"
    fi

    echo
    echo "WARNING: Replacing previous content:"
    ls $HARDLINK_DEST

    read -p "Do you wish to proceed? [y/n] " yn
    if ! echo "$yn" | grep -iq "^y" ;then
        echo "Cancelled"
        exit 1
    fi
    rm -rf $HARDLINK_DEST
  fi

  mkdir -p $HARDLINK_DEST

  pushd $HARDLINK_DEST
  ABS_HARDLINK_DEST=`pwd`
  popd

  echo
  echo Linking content from:
  echo $ABS_HARDLINK_SOURCE
  echo
  echo To:
  echo $HARDLINK_DEST
  echo

  pushd $ABS_HARDLINK_SOURCE
  pax -rwl . $ABS_HARDLINK_DEST
  popd

  echo
  echo "OK"
}

HARDLINK_SOURCE="speechly-client-net-standard-2.0/Speechly/SLUClient"
HARDLINK_DEST="speechly-unity/Assets/Speechly/SLUClient"

linkSource $HARDLINK_SOURCE $HARDLINK_DEST

HARDLINK_SOURCE="speechly-client-net-standard-2.0/Speechly/SLUClientTest"
HARDLINK_DEST="speechly-unity/Assets/SpeechlyExamples/AudioFileToSpeechly/SLUClientTest"

linkSource $HARDLINK_SOURCE $HARDLINK_DEST
