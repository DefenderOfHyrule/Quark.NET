#include <libusb-1.0/libusb.h>

void quark_fill_bulk_transfer(
    struct libusb_transfer *transfer,
    libusb_device_handle   *dev_handle,
    unsigned char           endpoint,
    unsigned char          *buffer,
    int                     length,
    libusb_transfer_cb_fn   callback,
    void                   *user_data,
    unsigned int            timeout)
{
    libusb_fill_bulk_transfer(transfer, dev_handle, endpoint,
                               buffer, length, callback, user_data, timeout);
}
