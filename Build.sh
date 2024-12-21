#!/bin/bash

IFS=$'\n'

PATH_ROOT=$(pwd)
PATH_BUILD=$PATH_ROOT/Build
if [ -d $PATH_BUILD ]; then
  rm -rf $PATH_BUILD/*
else
  mkdir -p $PATH_BUILD
fi

PATH_SOLUTION=$PATH_ROOT/LethalCompanyModTemplate.sln
if [ ! -f $PATH_SOLUTION ]; then
  echo "Could not find $PATH_SOLUTION, aborting..."
  exit 1
fi

PATH_SOLUTION_PROJECT=$(cat $PATH_SOLUTION | grep -Po '[a-zA-Z]+\\[a-zA-Z]+.csproj' | sed 's/\\/\//')
if [ -z $PATH_SOLUTION_PROJECT ]; then
  echo "Could not find a referenced project in the solution..."
  exit 1
fi

PATH_PROJECT_CSPROJ=$PATH_ROOT/$PATH_SOLUTION_PROJECT
if [ ! -f $PATH_PROJECT_CSPROJ ]; then
  echo "Could not find the reference project $PATH_PROJECT_CSPROJ, aborting..."
  exit 1
fi

PATH_PROJECT=$(dirname $PATH_PROJECT_CSPROJ)
PATH_PROJECT_ASSETS=$PATH_PROJECT/Assets
PATH_PROJECT_ASSETS_TO_COPY=$PATH_PROJECT_ASSETS/*
if [ -e $PATH_PROJECT_ASSETS_TO_COPY ]; then
  cp -r $PATH_PROJECT_ASSETS_TO_COPY $PATH_BUILD
fi

cd $PATH_PROJECT
dotnet tool restore
dotnet build -o bin

if [ ! $? -eq 0 ]; then
  exit 1
fi

PATH_PROJECT_BIN=$PATH_PROJECT/bin/$(basename $(echo $PATH_PROJECT_CSPROJ | sed -e 's/\.csproj//g')).dll
if [ ! -f $PATH_PROJECT_BIN ]; then
  echo "Could not find the binary library $PATH_PROJECT_BIN, aborting..."
  exit 1
fi

cp $PATH_PROJECT_BIN $PATH_BUILD