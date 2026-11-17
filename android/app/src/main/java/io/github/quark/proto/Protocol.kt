package io.github.quark.proto

object Protocol {
    const val BLOCK_SIZE = 0x1000
    const val INPUT_MAGIC = 0x49434C47
    const val OUTPUT_MAGIC = 0x4F434C47
    const val RESULT_SUCCESS = 0
    const val INVALID_CMD_ID = 0

    const val RESULT_EXCEPTION_CAUGHT = 0xBAF1
    const val RESULT_INVALID_INDEX = 0xBAF2
    const val RESULT_INVALID_FILE_MODE = 0xBAF3
    const val RESULT_SELECTION_CANCELLED = 0xBAF4

    const val PATH_TYPE_INVALID = 0
    const val PATH_TYPE_FILE = 1
    const val PATH_TYPE_DIRECTORY = 2

    const val FILE_MODE_READ = 1
    const val FILE_MODE_WRITE = 2
    const val FILE_MODE_APPEND = 3

    const val CMD_GET_DRIVE_COUNT = 1
    const val CMD_GET_DRIVE_INFO = 2
    const val CMD_STAT_PATH = 3
    const val CMD_GET_FILE_COUNT = 4
    const val CMD_GET_FILE = 5
    const val CMD_GET_DIRECTORY_COUNT = 6
    const val CMD_GET_DIRECTORY = 7
    const val CMD_START_FILE = 8
    const val CMD_READ_FILE = 9
    const val CMD_WRITE_FILE = 10
    const val CMD_END_FILE = 11
    const val CMD_CREATE = 12
    const val CMD_DELETE = 13
    const val CMD_RENAME = 14
    const val CMD_GET_SPECIAL_PATH_COUNT = 15
    const val CMD_GET_SPECIAL_PATH = 16
    const val CMD_SELECT_FILE = 17
    const val CMD_ANNOUNCE_CONSOLE_ID = 18

    const val USB_VENDOR_ID = 0x057E
    const val USB_PRODUCT_ID = 0x3000
    const val USB_ENDPOINT_OUT = 0x01
    const val USB_ENDPOINT_IN = 0x81

    const val TCP_PORT = 2313
}
