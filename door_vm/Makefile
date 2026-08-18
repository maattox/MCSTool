# VM2 control-plane C modules.
#
#   make            build the static library
#   make test       build and run the budget + state + mc_proto tests
#   make mcdoor     build the mcdoor_test server binary
#   make mccontrol  build the mccontrol daemon (mcdoor + HTTP API + UI)
#   make clean      remove build/
#
# The tests read the shared LA-budget fixture that the VM1 Python suite uses;
# override FIXTURE if you run make from somewhere other than vm2/.

CC = gcc
AR = ar
CFLAGS = -std=c11 -Wall -Wextra -O2 -Iinclude
DEPFLAGS = -MMD -MP
LDFLAGS =
LDLIBS =

ifeq ($(OS),Windows_NT)
EXE = .exe
LDLIBS += -lws2_32
else
LDLIBS += -pthread
endif

BUILD = build
LIB_SRC = src/budget.c src/state.c src/jsonmin.c src/mc_proto.c src/mcdoor.c src/control.c src/httpmini.c src/keepalive.c
OBJ = $(patsubst src/%.c,$(BUILD)/%.o,$(LIB_SRC))
LIB = $(BUILD)/libmcdoor.a

FIXTURE = ../tests/fixtures/budget_sessions.json
TEST_BINS = $(BUILD)/test_budget$(EXE) $(BUILD)/test_state$(EXE) $(BUILD)/test_mc_proto$(EXE)
MCDOOR_TEST = $(BUILD)/mcdoor_test$(EXE)
MCCONTROL = $(BUILD)/mccontrol$(EXE)

.PHONY: all test mcdoor mccontrol clean

all: $(LIB)

mcdoor: $(MCDOOR_TEST)

mccontrol: $(MCCONTROL)

$(LIB): $(OBJ)
	$(AR) rcs $@ $^

$(BUILD)/%.o: src/%.c | $(BUILD)
	$(CC) $(CFLAGS) $(DEPFLAGS) -c $< -o $@

$(BUILD)/test_%$(EXE): tests/test_%.c $(LIB) | $(BUILD)
	$(CC) $(CFLAGS) $(DEPFLAGS) $(LDFLAGS) $< $(LIB) $(LDLIBS) -o $@

$(BUILD)/mcdoor_test$(EXE): tests/mcdoor_test_main.c $(LIB) | $(BUILD)
	$(CC) $(CFLAGS) $(DEPFLAGS) $(LDFLAGS) $< $(LIB) $(LDLIBS) -o $@

$(BUILD)/mccontrol$(EXE): src/main.c $(LIB) | $(BUILD)
	$(CC) $(CFLAGS) $(DEPFLAGS) $(LDFLAGS) $< $(LIB) $(LDLIBS) -o $@

test: $(TEST_BINS)
	./$(BUILD)/test_budget$(EXE) $(FIXTURE) $(BUILD)/test_ledger.json
	./$(BUILD)/test_state$(EXE) $(BUILD)/test_state.json
	./$(BUILD)/test_mc_proto$(EXE)

clean:
	rm -rf $(BUILD)

$(BUILD):
	mkdir -p $(BUILD)

-include $(OBJ:.o=.d)
