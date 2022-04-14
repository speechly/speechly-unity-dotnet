#!/bin/bash

nuget install docfx.console -x
open http://localhost:8080
mono ./docfx.console/tools/docfx.exe --serve
