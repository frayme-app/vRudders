// Based on the Microsoft vhidmini2 UMDF sample.
// Copyright (c) Microsoft Corporation. All rights reserved.
// Modifications: VRudders, 2026. See LICENSE-MS-PL.txt.
#include <windows.h>
#pragma warning(push)
#pragma warning(disable:4324) // Intentional alignment in WDK structures.
#include <wdf.h>
#include <hidport.h>
#pragma warning(pop)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD AddDevice;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL DeviceControl;
EVT_WDF_TIMER ReportTimer;

// Development identifiers from Microsoft's sample; not assigned USB vendor IDs.
#define RUDDER_VID 0xDEED
#define RUDDER_PID 0xFEED
#define CENTER 32768
#define TIMEOUT_MS 300

#pragma pack(push, 1)
typedef struct { UCHAR Id; USHORT X, Y, Z; UCHAR Buttons; } INPUT_REPORT;
typedef struct { UCHAR Id, Version; USHORT Z; UCHAR Active; } FEATURE_REPORT;
#pragma pack(pop)
C_ASSERT(sizeof(INPUT_REPORT) == 8);
C_ASSERT(sizeof(FEATURE_REPORT) == 5);

typedef struct {
    WDFQUEUE Reads;
    WDFWAITLOCK Lock;
    USHORT Z;
    ULONGLONG LastUpdate;
} DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, Context);

static const UCHAR ReportDescriptor[] = {
    0x05,0x01, 0x09,0x04, 0xA1,0x01, // Generic desktop / Joystick
    0x85,0x01,                       // Input report 1
    0x15,0x00, 0x27,0xFF,0xFF,0x00,0x00,
    0x75,0x10, 0x95,0x03,
    0x09,0x30, 0x09,0x31, 0x09,0x32, 0x81,0x02, // X, Y, Z
    0x05,0x09, 0x19,0x01, 0x29,0x01,
    0x15,0x00, 0x25,0x01, 0x75,0x01, 0x95,0x01, 0x81,0x02,
    0x75,0x07, 0x95,0x01, 0x81,0x03, // one neutral button + padding
    0x06,0x00,0xFF, 0x09,0x01, 0x85,0x02, // vendor feature report 2
    0x15,0x00, 0x26,0xFF,0x00, 0x75,0x08, 0x95,0x04, 0xB1,0x02,
    0xC0
};
static const HID_DESCRIPTOR Descriptor = {
    sizeof(HID_DESCRIPTOR), HID_HID_DESCRIPTOR_TYPE, 0x0111, 0, 1,
    {{ HID_REPORT_DESCRIPTOR_TYPE, sizeof(ReportDescriptor) }}
};

static INPUT_REPORT Snapshot(DEVICE_CONTEXT* ctx)
{
    INPUT_REPORT report = { 1, CENTER, CENTER, CENTER, 0 };
    WdfWaitLockAcquire(ctx->Lock, NULL);
    if (GetTickCount64() - ctx->LastUpdate <= TIMEOUT_MS) report.Z = ctx->Z;
    WdfWaitLockRelease(ctx->Lock);
    return report;
}

static NTSTATUS CopyToRequest(WDFREQUEST request, const void* source, size_t size)
{
    void* target;
    NTSTATUS status = WdfRequestRetrieveOutputBuffer(request, size, &target, NULL);
    if (!NT_SUCCESS(status)) return status;
    memcpy(target, source, size);
    WdfRequestSetInformation(request, size);
    return STATUS_SUCCESS;
}

// MsHidUmdf marshals HID_XFER_PACKET embedded pointers into WDF buffers.
// On writes, the report ID is encoded in the output buffer LENGTH.
static NTSTATUS GetPacket(WDFREQUEST request, BOOLEAN reading, HID_XFER_PACKET* packet)
{
    WDFMEMORY input, output;
    size_t inputSize, outputSize;
    UCHAR* inBuffer;
    void* outBuffer;
    NTSTATUS status = WdfRequestRetrieveInputMemory(request, &input);
    if (!NT_SUCCESS(status)) return status;
    status = WdfRequestRetrieveOutputMemory(request, &output);
    if (!NT_SUCCESS(status)) return status;
    inBuffer = WdfMemoryGetBuffer(input, &inputSize);
    outBuffer = WdfMemoryGetBuffer(output, &outputSize);
    if (reading) {
        if (inputSize < 1 || outputSize > MAXDWORD) return STATUS_INVALID_BUFFER_SIZE;
        packet->reportId = inBuffer[0];
        packet->reportBuffer = outBuffer;
        packet->reportBufferLen = (ULONG)outputSize;
    } else {
        if (outputSize > 255 || inputSize > MAXDWORD) return STATUS_INVALID_BUFFER_SIZE;
        packet->reportId = (UCHAR)outputSize;
        packet->reportBuffer = inBuffer;
        packet->reportBufferLen = (ULONG)inputSize;
    }
    return STATUS_SUCCESS;
}

NTSTATUS DriverEntry(PDRIVER_OBJECT driver, PUNICODE_STRING path)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, AddDevice);
    return WdfDriverCreate(driver, path, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}

NTSTATUS AddDevice(WDFDRIVER driver, PWDFDEVICE_INIT init)
{
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attrs;
    WDF_IO_QUEUE_CONFIG queue;
    WDF_TIMER_CONFIG timerConfig;
    WDFTIMER timer;
    DEVICE_CONTEXT* ctx;
    NTSTATUS status;
    UNREFERENCED_PARAMETER(driver);
    WdfFdoInitSetFilter(init);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attrs, DEVICE_CONTEXT);
    status = WdfDeviceCreate(&init, &attrs, &device);
    if (!NT_SUCCESS(status)) return status;
    ctx = Context(device);
    ctx->Z = CENTER;
    WDF_OBJECT_ATTRIBUTES_INIT(&attrs);
    attrs.ParentObject = device;
    status = WdfWaitLockCreate(&attrs, &ctx->Lock);
    if (!NT_SUCCESS(status)) return status;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue, WdfIoQueueDispatchParallel);
    queue.EvtIoDeviceControl = DeviceControl;
    status = WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) return status;
    WDF_IO_QUEUE_CONFIG_INIT(&queue, WdfIoQueueDispatchManual);
    queue.PowerManaged = WdfFalse;
    status = WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, &ctx->Reads);
    if (!NT_SUCCESS(status)) return status;
    WDF_TIMER_CONFIG_INIT_PERIODIC(&timerConfig, ReportTimer, 8);
    timerConfig.AutomaticSerialization = FALSE;
    WDF_OBJECT_ATTRIBUTES_INIT(&attrs);
    attrs.ParentObject = device;
    status = WdfTimerCreate(&timerConfig, &attrs, &timer);
    if (!NT_SUCCESS(status)) return status;
    WdfTimerStart(timer, WDF_REL_TIMEOUT_IN_MS(8));
    return STATUS_SUCCESS;
}

void ReportTimer(WDFTIMER timer)
{
    DEVICE_CONTEXT* ctx = Context((WDFDEVICE)WdfTimerGetParentObject(timer));
    WDFREQUEST request;
    INPUT_REPORT report = Snapshot(ctx);
    // Bounded work per tick; cancellation and removal are managed by WDF.
    for (int i = 0; i < 32 && NT_SUCCESS(WdfIoQueueRetrieveNextRequest(ctx->Reads, &request)); ++i) {
        NTSTATUS status = CopyToRequest(request, &report, sizeof(report));
        WdfRequestComplete(request, status);
    }
}

void DeviceControl(WDFQUEUE queue, WDFREQUEST request, size_t outLength, size_t inLength, ULONG code)
{
    DEVICE_CONTEXT* ctx = Context(WdfIoQueueGetDevice(queue));
    NTSTATUS status = STATUS_NOT_SUPPORTED;
    HID_XFER_PACKET packet;
    UNREFERENCED_PARAMETER(outLength);
    UNREFERENCED_PARAMETER(inLength);
    switch (code) {
    case IOCTL_HID_GET_DEVICE_DESCRIPTOR:
        status = CopyToRequest(request, &Descriptor, sizeof(Descriptor)); break;
    case IOCTL_HID_GET_REPORT_DESCRIPTOR:
        status = CopyToRequest(request, ReportDescriptor, sizeof(ReportDescriptor)); break;
    case IOCTL_HID_GET_DEVICE_ATTRIBUTES: {
        HID_DEVICE_ATTRIBUTES a = { sizeof(HID_DEVICE_ATTRIBUTES), RUDDER_VID, RUDDER_PID, 0x0001 };
        status = CopyToRequest(request, &a, sizeof(a)); break;
    }
    case IOCTL_HID_GET_STRING: {
        ULONG* id;
        const WCHAR* text = NULL;
        status = WdfRequestRetrieveInputBuffer(request, sizeof(ULONG), (void**)&id, NULL);
        if (!NT_SUCCESS(status)) break;
        switch (*id & 0xFFFF) {
        case HID_STRING_ID_IMANUFACTURER: text = L"VRudders"; break;
        case HID_STRING_ID_IPRODUCT: text = L"VRudders Yaw"; break;
        case HID_STRING_ID_ISERIALNUMBER: text = L"VRUDDERS-POC-0001"; break;
        }
        status = text ? CopyToRequest(request, text, (wcslen(text) + 1) * sizeof(WCHAR)) : STATUS_INVALID_PARAMETER;
        break;
    }
    case IOCTL_HID_READ_REPORT:
        status = WdfRequestForwardToIoQueue(request, ctx->Reads);
        if (NT_SUCCESS(status)) return;
        break;
    case IOCTL_UMDF_HID_SET_FEATURE:
        status = GetPacket(request, FALSE, &packet);
        if (NT_SUCCESS(status)) {
            FEATURE_REPORT report;
            if (packet.reportId != 2 || packet.reportBufferLen != sizeof(report)) { status = STATUS_INVALID_PARAMETER; break; }
            memcpy(&report, packet.reportBuffer, sizeof(report));
            if (report.Id != 2 || report.Version != 1 || report.Active > 1) { status = STATUS_INVALID_PARAMETER; break; }
            WdfWaitLockAcquire(ctx->Lock, NULL);
            ctx->Z = report.Active ? report.Z : CENTER;
            ctx->LastUpdate = GetTickCount64();
            WdfWaitLockRelease(ctx->Lock);
            WdfRequestSetInformation(request, sizeof(report));
        }
        break;
    case IOCTL_UMDF_HID_GET_INPUT_REPORT:
    case IOCTL_UMDF_HID_GET_FEATURE:
        status = GetPacket(request, TRUE, &packet);
        if (NT_SUCCESS(status)) {
            INPUT_REPORT input = Snapshot(ctx);
            if (code == IOCTL_UMDF_HID_GET_INPUT_REPORT && packet.reportId == 1 && packet.reportBufferLen >= sizeof(input)) {
                memcpy(packet.reportBuffer, &input, sizeof(input));
                WdfRequestSetInformation(request, sizeof(input));
            } else if (code == IOCTL_UMDF_HID_GET_FEATURE && packet.reportId == 2 && packet.reportBufferLen >= sizeof(FEATURE_REPORT)) {
                FEATURE_REPORT feature = { 2, 1, input.Z, 0 };
                memcpy(packet.reportBuffer, &feature, sizeof(feature));
                WdfRequestSetInformation(request, sizeof(feature));
            } else status = STATUS_INVALID_PARAMETER;
        }
        break;
    }
    WdfRequestComplete(request, status);
}
