<?xml version='1.0' encoding='UTF-8'?><wsdl:definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:tns="http://niem.sustain.com/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/" xmlns:ns11="http://schemas.xmlsoap.org/soap/http" xmlns:ns1="urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0" name="CourtRecordMDEService" targetNamespace="http://niem.sustain.com/">
  <wsdl:import location="https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/CourtRecord/?wsdl=CourtRecordMDEPort.wsdl" namespace="urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0">
    </wsdl:import>
  <wsdl:binding name="CourtRecordMDEServiceSoapBinding" type="ns1:CourtRecordMDEPort">
    <soap:binding style="document" transport="http://schemas.xmlsoap.org/soap/http"/>
    <wsdl:operation name="RecordFiling">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="RecordFiling">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="RecordFilingResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="RecordCase">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="RecordCase">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="RecordCaseResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetCase">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetCase">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetCaseResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
    <wsdl:operation name="GetCaseList">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetCaseList">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetCaseListResponse">
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
    <wsdl:operation name="GetServiceInformation">
      <soap:operation soapAction="" style="document"/>
      <wsdl:input name="GetServiceInformation">
        <soap:body use="literal"/>
      </wsdl:input>
      <wsdl:output name="GetServiceInformationResponse">
        <soap:body use="literal"/>
      </wsdl:output>
    </wsdl:operation>
  </wsdl:binding>
  <wsdl:service name="CourtRecordMDEService">
    <wsdl:port binding="tns:CourtRecordMDEServiceSoapBinding" name="CourtRecordMDEPort">
      <soap:address location="https://aux-pub-efm-madera-ca.ecourt.com/ws/soap/niem/CourtRecord/"/>
    </wsdl:port>
  </wsdl:service>
</wsdl:definitions>