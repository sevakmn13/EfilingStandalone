<?xml version='1.0' encoding='UTF-8'?><wsdl:definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:tns="http://niem.sustain.com/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/" xmlns:ns20="http://schemas.xmlsoap.org/soap/http" xmlns:ns1="urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0" name="FilingReviewMDEService" targetNamespace="http://niem.sustain.com/">
  <wsdl:import location="https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/?wsdl=FilingReviewMDEPort.wsdl" namespace="urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0">
    </wsdl:import>
  <wsdl:binding name="FilingReviewMDEServiceSoapBinding" type="ns1:FilingReviewMDEPort">
    <soap:binding style="document" transport="http://schemas.xmlsoap.org/soap/http"/>
    <wsdl:operation name="GetFilingStatus">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetFilingStatus">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetFilingStatusResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetPolicy">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetPolicy">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetPolicyResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="NotifyDocketingComplete">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="NotifyDocketingComplete">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="NotifyDocketingCompleteResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetFilingList">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetFilingList">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetFilingListResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetNFRC">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetNFRC">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetNFRCResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetChargedAmount">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetChargedAmount">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetChargedAmountResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="ReviewFiling">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="ReviewFiling">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="ReviewFilingResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetDocument">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetDocument">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetDocumentResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetRecordingStatus">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetRecordingStatus">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetRecordingStatusResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetFeesCalculation">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetFeesCalculation">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetFeesCalculationResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
  </wsdl:binding>
  <wsdl:service name="FilingReviewMDEService">
    <wsdl:port binding="tns:FilingReviewMDEServiceSoapBinding" name="FilingReviewMDEPort">
      <soap:address location="https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/FilingReview/"/>
    </wsdl:port>
  </wsdl:service>
</wsdl:definitions>