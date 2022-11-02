#!/bin/bash

nuget install docfx.console -Version 2.59.3.0 -x
open http://localhost:8080
mono ./docfx.console/tools/docfx.exe --serve
