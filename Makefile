DOTNET ?= dotnet
SOLUTION := MSNRBFEditor.slnx
GUI_PROJECT := gui/MSNRBFEditor.Gui.fsproj
CLI_PROJECT := cli/MSNRBFEditor.Cli.fsproj
SAVE ?= savefile.sav
JSON ?= savefile.json
WIN_RID ?= win-x64
LINUX_RID ?= linux-x64
DIST ?= dist
WIN_CLI_DIR := $(DIST)/$(WIN_RID)-cli
WIN_GUI_DIR := $(DIST)/$(WIN_RID)-gui
LINUX_CLI_DIR := $(DIST)/$(LINUX_RID)-cli
LINUX_GUI_DIR := $(DIST)/$(LINUX_RID)-gui

.PHONY: all build restore clean distclean gui run-gui cli run-cli run-cli-stdout run-cli-compact publish-win publish-win-cli publish-win-gui publish-linux publish-linux-cli publish-linux-gui help

all: build

## Restore NuGet packages
restore:
	$(DOTNET) restore $(SOLUTION)

## Build all projects
build:
	$(DOTNET) build $(SOLUTION)

## Clean build outputs
clean:
	$(DOTNET) clean $(SOLUTION)

## Remove build output directories as well
distclean: clean
	rm -rf cli/bin cli/obj core/bin core/obj gui/bin gui/obj $(DIST)

## Run the MS-NRBF Editor GUI (SAVE=path/to/file.sav)
gui run-gui:
	$(DOTNET) run --project $(GUI_PROJECT) -- $(SAVE)

## Convert a save to pretty JSON (SAVE=... JSON=...)
cli run-cli:
	$(DOTNET) run --project $(CLI_PROJECT) -- $(SAVE) $(JSON)

## Convert a save to pretty JSON on stdout
run-cli-stdout:
	$(DOTNET) run --project $(CLI_PROJECT) -- $(SAVE)

## Convert a save to compact JSON on stdout
run-cli-compact:
	$(DOTNET) run --project $(CLI_PROJECT) -- $(SAVE) --compact

## Publish self-contained Windows binaries for CLI and GUI
publish-win: publish-win-cli publish-win-gui

## Publish the CLI as a self-contained Windows executable
publish-win-cli:
	$(DOTNET) publish $(CLI_PROJECT) \
		-c Release -r $(WIN_RID) --self-contained true \
		-p:PublishSingleFile=true \
		-o $(WIN_CLI_DIR)

## Publish the GUI as a self-contained Windows executable
publish-win-gui:
	$(DOTNET) publish $(GUI_PROJECT) \
		-c Release -r $(WIN_RID) --self-contained true \
		-p:PublishSingleFile=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-o $(WIN_GUI_DIR)

## Publish self-contained Linux binaries for CLI and GUI
publish-linux: publish-linux-cli publish-linux-gui

## Publish the CLI as a self-contained Linux executable
publish-linux-cli:
	$(DOTNET) publish $(CLI_PROJECT) \
		-c Release -r $(LINUX_RID) --self-contained true \
		-p:PublishSingleFile=true \
		-o $(LINUX_CLI_DIR)

## Publish the GUI as a self-contained Linux executable
publish-linux-gui:
	$(DOTNET) publish $(GUI_PROJECT) \
		-c Release -r $(LINUX_RID) --self-contained true \
		-p:PublishSingleFile=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-o $(LINUX_GUI_DIR)

## Show this help
help:
	@echo "Targets:"
	@echo "  make build              Build all projects"
	@echo "  make gui                Run the MS-NRBF Editor GUI (SAVE=file.sav)"
	@echo "  make cli                Convert to JSON (SAVE=file.sav JSON=file.json)"
	@echo "  make run-cli-stdout     Convert to pretty JSON on stdout"
	@echo "  make run-cli-compact    Convert to compact JSON on stdout"
	@echo "  make publish-win        Publish Windows x64 CLI + GUI binaries"
	@echo "  make publish-win-cli    Publish Windows x64 CLI binary"
	@echo "  make publish-win-gui    Publish Windows x64 GUI binary"
	@echo "  make publish-linux      Publish Linux x64 CLI + GUI binaries"
	@echo "  make publish-linux-cli  Publish Linux x64 CLI binary"
	@echo "  make publish-linux-gui  Publish Linux x64 GUI binary"
	@echo "  make clean              Clean build outputs"
	@echo "  make distclean          Remove all bin/obj/dist directories"
	@echo ""
	@echo "Examples:"
	@echo "  make gui SAVE=savefile.sav"
	@echo "  make cli SAVE=savefile.sav JSON=savefile.json"
	@echo "  make publish-win WIN_RID=win-x64"
	@echo "  make publish-win WIN_RID=win-arm64 DIST=dist-arm"
	@echo "  make publish-linux LINUX_RID=linux-x64"
	@echo "  make publish-linux LINUX_RID=linux-arm64 DIST=dist-arm"
