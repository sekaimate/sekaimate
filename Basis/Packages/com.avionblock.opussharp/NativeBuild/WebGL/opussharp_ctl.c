#include "opus.h"
#include "opus_multistream.h"

int opussharp_encoder_ctl(OpusEncoder *state, int request) {
    return opus_encoder_ctl(state, request);
}

int opussharp_encoder_ctl_i(OpusEncoder *state, int request, int value) {
    return opus_encoder_ctl(state, request, value);
}

int opussharp_encoder_ctl_p(OpusEncoder *state, int request, void *value) {
    return opus_encoder_ctl(state, request, value);
}

int opussharp_encoder_ctl_pi(OpusEncoder *state, int request, void *value, int second_value) {
    return opus_encoder_ctl(state, request, value, second_value);
}

int opussharp_encoder_ctl_ip(OpusEncoder *state, int request, int value, void *second_value) {
    return opus_encoder_ctl(state, request, value, second_value);
}

int opussharp_encoder_ctl_pp(OpusEncoder *state, int request, void *value, void *second_value) {
    return opus_encoder_ctl(state, request, value, second_value);
}

int opussharp_decoder_ctl(OpusDecoder *state, int request) {
    return opus_decoder_ctl(state, request);
}

int opussharp_decoder_ctl_i(OpusDecoder *state, int request, int value) {
    return opus_decoder_ctl(state, request, value);
}

int opussharp_decoder_ctl_p(OpusDecoder *state, int request, void *value) {
    return opus_decoder_ctl(state, request, value);
}

int opussharp_dred_decoder_ctl(OpusDREDDecoder *state, int request) {
    return opus_dred_decoder_ctl(state, request);
}

int opussharp_dred_decoder_ctl_p(OpusDREDDecoder *state, int request, void *value) {
    return opus_dred_decoder_ctl(state, request, value);
}

int opussharp_ms_encoder_ctl(OpusMSEncoder *state, int request) {
    return opus_multistream_encoder_ctl(state, request);
}

int opussharp_ms_encoder_ctl_i(OpusMSEncoder *state, int request, int value) {
    return opus_multistream_encoder_ctl(state, request, value);
}

int opussharp_ms_encoder_ctl_p(OpusMSEncoder *state, int request, void *value) {
    return opus_multistream_encoder_ctl(state, request, value);
}

int opussharp_ms_encoder_ctl_pi(OpusMSEncoder *state, int request, void *value, int second_value) {
    return opus_multistream_encoder_ctl(state, request, value, second_value);
}

int opussharp_ms_encoder_ctl_ip(OpusMSEncoder *state, int request, int value, void *second_value) {
    return opus_multistream_encoder_ctl(state, request, value, second_value);
}

int opussharp_ms_encoder_ctl_pp(OpusMSEncoder *state, int request, void *value, void *second_value) {
    return opus_multistream_encoder_ctl(state, request, value, second_value);
}

int opussharp_ms_decoder_ctl(OpusMSDecoder *state, int request) {
    return opus_multistream_decoder_ctl(state, request);
}

int opussharp_ms_decoder_ctl_i(OpusMSDecoder *state, int request, int value) {
    return opus_multistream_decoder_ctl(state, request, value);
}

int opussharp_ms_decoder_ctl_p(OpusMSDecoder *state, int request, void *value) {
    return opus_multistream_decoder_ctl(state, request, value);
}
